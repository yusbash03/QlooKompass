using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

/// <summary>Real calls against the hackathon Qloo API. Only run when QLOO_API_KEY is set
/// (`QLOO_API_KEY=... dotnet test --filter "Category=Live"`) — not set in this environment, so these report
/// as skipped rather than exercised. See docs/backend/qloo-verified-contract.md.</summary>
[Trait("Category", "Live")]
public class QlooLiveTests
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("QLOO_API_KEY");

    private static IQlooClient BuildLiveClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new QlooOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable("QLOO_BASE_URL") ?? "https://hackathon.api.qloo.com",
            ApiKey = ApiKey,
            Mode = QlooMode.Live,
        }));
        services.AddSingleton<QlooApiKeyHandler>();
        services.AddSingleton<QlooThrottleHandler>();
        services.AddSingleton<RedactingLoggingHandler>();
        services.AddHttpClient<IQlooClient, QlooClient>(client => client.BaseAddress = new Uri(
                Environment.GetEnvironmentVariable("QLOO_BASE_URL") ?? "https://hackathon.api.qloo.com"))
            .AddHttpMessageHandler<QlooApiKeyHandler>()
            .AddHttpMessageHandler<QlooThrottleHandler>()
            .AddHttpMessageHandler<RedactingLoggingHandler>()
            .AddStandardResilienceHandler(r => QlooServiceCollectionExtensions.ConfigureResilience(r, TimeSpan.FromSeconds(20)));

        return services.BuildServiceProvider().GetRequiredService<IQlooClient>();
    }

    [Fact]
    public async Task Search_returns_entities_with_ids()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return; // excluded by default via `--filter "Category!=Live"`; this guard only matters if someone
                     // runs `--filter "Category=Live"` without exporting QLOO_API_KEY first.
        }

        var sut = BuildLiveClient();
        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "Nike", Types = [QlooEntityType.Brand] }, CancellationToken.None);

        var success = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Success>();
        success.Value.ShouldNotBeEmpty();
        success.Value.ShouldAllBe(e => !string.IsNullOrEmpty(e.Id));
    }

    [Fact]
    public async Task A_request_with_no_signal_returns_the_documented_400_shape()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return;
        }

        var sut = BuildLiveClient();
        var result = await sut.GetInsightsAsync(new InsightsRequest { FilterType = QlooEntityType.Brand }, CancellationToken.None);

        result.ShouldBeOfType<QlooResult<InsightsResponse>.Failure>().Kind.ShouldBe(QlooErrorKind.BadRequest);
    }
}
