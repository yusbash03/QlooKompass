using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Domain.Plans;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Generation;
using KultureCompass.Infrastructure.Persistence;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Generation;

/// <summary>Regressions found running the live app on 2026-10-26.</summary>
[Collection(PostgresCollection.Name)]
public class WorkerAndHttpLifetimeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Qloo_client_can_be_created_again_after_the_factory_rotates_its_handler_chain()
    {
        // Bug: handlers were singletons; IHttpClientFactory rebuilds the chain every HandlerLifetime and threw
        // "The 'InnerHandler' property must be null" — every plan after the first ~2 minutes crashed.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQlooClient(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qloo:Mode"] = "Live",
            ["Qloo:ApiKey"] = "synthetic-test-key-not-real",
            ["Qloo:BaseUrl"] = "https://example.test",
        }).Build());
        services.Configure<HttpClientFactoryOptions>(nameof(IQlooClient), o => o.HandlerLifetime = TimeSpan.FromSeconds(1));
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IQlooClient>().ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromMilliseconds(1100)); // past the (minimum) 1 s lifetime → chain is rebuilt
        Should.NotThrow(() => provider.GetRequiredService<IQlooClient>());
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Should.NotThrow(() => provider.GetRequiredService<IQlooClient>());
    }

    [Fact]
    public async Task A_run_that_crashes_before_the_pipeline_starts_fails_the_plan_instead_of_leaving_it_Queued()
    {
        var planId = await PipelineTestHost.CreateQueuedPlanAsync(fixture, PipelineTestHost.LagosStreetwearBrief());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString).UseSnakeCaseNamingConvention());
        services.AddScoped<IGrowthPlanStore, GrowthPlanStore>();
        services.AddScoped<GrowthPlanPipeline>(_ => throw new InvalidOperationException("synthetic: dependency construction failed"));
        services.AddSingleton<ChannelGenerationQueue>();
        await using var provider = services.BuildServiceProvider();

        var queue = provider.GetRequiredService<ChannelGenerationQueue>();
        var worker = new GenerationWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new GenerationSettings { RecoverOnStartup = false }), NullLogger<GenerationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(planId, CancellationToken.None);

        GrowthPlan plan = null!;
        for (var i = 0; i < 100; i++)
        {
            plan = await PipelineTestHost.ReloadAsync(fixture, planId);
            if (plan.IsTerminal)
                break;
            await Task.Delay(50);
        }
        await worker.StopAsync(CancellationToken.None);

        plan.Status.ShouldBe(GenerationStatus.Failed);
        plan.Error!.Code.ShouldBe(PlanErrorCode.InternalError);
        plan.Error.Message.ShouldNotContain("synthetic: dependency");
    }
}
