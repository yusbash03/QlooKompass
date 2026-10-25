using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KultureCompass.Application.Abstractions;
using KultureCompass.Domain.Plans;
using KultureCompass.Infrastructure.Generation;
using KultureCompass.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KultureCompass.Api.Tests;

/// <summary>Swallows enqueues so plans stay Queued — lets tests hold a plan "in progress" deterministically.</summary>
internal sealed class HoldingQueue : IGenerationQueue
{
    public ValueTask EnqueueAsync(Guid planId, CancellationToken ct) => ValueTask.CompletedTask;
}

[Collection(ApiCollection.Name)]
public class GrowthPlanTests(ApiFactory factory)
{
    private const string PlansPath = "/api/v1/growth-plans";
    private static readonly HashSet<string> Terminal = ["Completed", "CompletedWithLimitations", "Failed"];

    private readonly HttpClient _client = factory.CreateApiClient();

    /// <summary>Same app + same database, but the worker never receives work and startup recovery is off.</summary>
    private HttpClient HoldingClient(int? dailyLimit = null) => factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Generation:RecoverOnStartup", "false");
            if (dailyLimit is { } limit)
                b.UseSetting("Generation:DailyLimitPerUser", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.ConfigureTestServices(s => s.AddSingleton<IGenerationQueue, HoldingQueue>());
        })
        .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<Guid> CreateProfileAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/business-profiles").WithBearer(token);
        request.Content = JsonContent.Create(new
        {
            brandName = "Synthetic Eko Threads",
            category = "Streetwear",
            productsDescription = "Graphic tees and caps",
            primaryLocation = "Lagos, Nigeria",
            marketingObjective = "Sales",
            referenceEntities = new[] { new { name = "Synthetic Afrobeat Star", kindHint = "Artist" } },
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> StartAsync(HttpClient client, string token, Guid profileId, string? key, object? options = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PlansPath).WithBearer(token);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(new { businessProfileId = profileId, options });
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string url) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url).WithBearer(token));

    private static async Task<JsonElement> WaitForTerminalAsync(HttpClient client, string token, Guid planId)
    {
        for (var i = 0; i < 150; i++)
        {
            var status = await (await GetAsync(client, token, $"{PlansPath}/{planId}/status")).Content.ReadFromJsonAsync<JsonElement>();
            if (Terminal.Contains(status.GetProperty("status").GetString()!))
                return status;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Plan {planId} did not reach a terminal state.");
    }

    [Fact]
    public async Task Post_returns_202_and_the_worker_completes_a_grounded_mock_plan_end_to_end()
    {
        var session = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, session.AccessToken);

        var start = await StartAsync(_client, session.AccessToken, profileId, Guid.NewGuid().ToString());

        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var accepted = await start.Content.ReadFromJsonAsync<JsonElement>();
        var planId = accepted.GetProperty("id").GetGuid();
        start.Headers.Location!.OriginalString.ShouldBe($"{PlansPath}/{planId}");
        accepted.GetProperty("statusUrl").GetString().ShouldBe($"{PlansPath}/{planId}/status");

        var status = await WaitForTerminalAsync(_client, session.AccessToken, planId);
        status.GetProperty("status").GetString().ShouldNotBe("Failed", status.ToString());
        var stages = status.GetProperty("stages").EnumerateArray().ToList();
        stages.Count.ShouldBe(8);
        stages.ShouldAllBe(s => s.GetProperty("state").GetString() == "Done");

        var plan = await (await GetAsync(_client, session.AccessToken, $"{PlansPath}/{planId}")).Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("isMock").GetBoolean().ShouldBeTrue();
        plan.GetProperty("isSample").GetBoolean().ShouldBeFalse();
        plan.GetProperty("qlooMode").GetString().ShouldBe("Mock");
        plan.GetProperty("brandName").GetString().ShouldBe("Synthetic Eko Threads");
        var evidenceKeys = plan.GetProperty("evidence").EnumerateArray().Select(e => e.GetProperty("key").GetString()).ToHashSet();
        evidenceKeys.Count.ShouldBeGreaterThanOrEqualTo(5);
        plan.GetProperty("evidence").EnumerateArray().ShouldAllBe(e => e.GetProperty("name").GetString()!.StartsWith("Synthetic "));
        plan.GetProperty("insights").GetArrayLength().ShouldBe(3);
        foreach (var section in new[] { "insights", "collaborations", "campaigns" })
        {
            foreach (var item in plan.GetProperty(section).EnumerateArray())
                item.GetProperty("evidenceKeys").EnumerateArray().ShouldAllBe(k => evidenceKeys.Contains(k.GetString()));
        }
        plan.GetProperty("experiments")[0].GetProperty("nextDecision").GetProperty("ifSuccess").GetString().ShouldNotBeNullOrEmpty();
        plan.GetProperty("summary").GetProperty("headline").GetString()!.ShouldStartWith("[MOCK]");
        plan.GetProperty("stats").GetProperty("llmCalls").GetInt32().ShouldBe(2);
        plan.TryGetProperty("ownerId", out _).ShouldBeFalse();

        // Replay after completion → 200 with the same plan.
        var replay = await StartAsync(_client, session.AccessToken, profileId, await IdempotencyKeyOf(planId));
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ShouldBe(planId);
    }

    private async Task<string> IdempotencyKeyOf(Guid planId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.GrowthPlans.FindAsync(planId))!.IdempotencyKey;
    }

    [Fact]
    public async Task Replay_with_the_same_key_and_body_returns_the_same_plan_and_a_different_body_is_409()
    {
        var client = HoldingClient();
        var session = await client.RegisterAsync();
        var profileId = await CreateProfileAsync(client, session.AccessToken);
        var key = Guid.NewGuid().ToString();

        var first = await StartAsync(client, session.AccessToken, profileId, key);
        var second = await StartAsync(client, session.AccessToken, profileId, key, new { includeCollaborations = true, includeCampaigns = true, maxCampaigns = 3 });
        var different = await StartAsync(client, session.AccessToken, profileId, key, new { maxCampaigns = 1 });

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted); // explicit defaults == omitted options
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()
            .ShouldBe((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        different.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await different.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("idempotency_key_reused");
    }

    [Theory]
    [InlineData(null, "idempotency_key_missing")]
    [InlineData("not-a-uuid", "validation_failed")]
    public async Task Missing_or_malformed_Idempotency_Key_is_400(string? key, string code)
    {
        var session = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, session.AccessToken);

        var response = await StartAsync(_client, session.AccessToken, profileId, key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Invalid_body_is_400_and_another_owners_profile_is_404()
    {
        var owner = await _client.RegisterAsync();
        var intruder = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, owner.AccessToken);

        var badOptions = await StartAsync(_client, owner.AccessToken, profileId, Guid.NewGuid().ToString(), new { maxCampaigns = 9 });
        var notMine = await StartAsync(_client, intruder.AccessToken, profileId, Guid.NewGuid().ToString());

        badOptions.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badOptions.ReadProblemAsync()).GetProperty("errors").TryGetProperty("options.maxCampaigns", out _).ShouldBeTrue();
        notMine.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Second_plan_for_a_profile_while_one_is_active_is_409_plan_in_progress()
    {
        var client = HoldingClient();
        var session = await client.RegisterAsync();
        var profileId = await CreateProfileAsync(client, session.AccessToken);

        (await StartAsync(client, session.AccessToken, profileId, Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var second = await StartAsync(client, session.AccessToken, profileId, Guid.NewGuid().ToString());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("plan_in_progress");
    }

    [Fact]
    public async Task Daily_quota_is_enforced_with_429_and_Retry_After_and_reported_by_me_quota()
    {
        var client = HoldingClient(dailyLimit: 2);
        var session = await client.RegisterAsync();

        for (var i = 0; i < 2; i++)
        {
            var profile = await CreateProfileAsync(client, session.AccessToken);
            (await StartAsync(client, session.AccessToken, profile, Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var third = await StartAsync(client, session.AccessToken, await CreateProfileAsync(client, session.AccessToken), Guid.NewGuid().ToString());

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await third.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("generation_quota_exceeded");
        var retryAfter = int.Parse(third.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        retryAfter.ShouldBeInRange(1, 86_400);

        var quota = await (await GetAsync(client, session.AccessToken, "/api/v1/me/quota")).Content.ReadFromJsonAsync<JsonElement>();
        quota.GetProperty("dailyLimit").GetInt32().ShouldBe(2);
        quota.GetProperty("usedToday").GetInt32().ShouldBe(2);
        quota.GetProperty("remainingToday").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Content_is_hidden_until_terminal_and_pending_stages_are_listed()
    {
        var client = HoldingClient();
        var session = await client.RegisterAsync();
        var profileId = await CreateProfileAsync(client, session.AccessToken);
        var planId = (await (await StartAsync(client, session.AccessToken, profileId, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var plan = await (await GetAsync(client, session.AccessToken, $"{PlansPath}/{planId}")).Content.ReadFromJsonAsync<JsonElement>();
        var status = await (await GetAsync(client, session.AccessToken, $"{PlansPath}/{planId}/status")).Content.ReadFromJsonAsync<JsonElement>();

        plan.GetProperty("status").GetString().ShouldBe("Queued");
        plan.GetProperty("summary").ValueKind.ShouldBe(JsonValueKind.Null);
        plan.GetProperty("evidence").GetArrayLength().ShouldBe(0);
        plan.GetProperty("briefSnapshot").GetProperty("brandName").GetString().ShouldBe("Synthetic Eko Threads");
        status.GetProperty("stages").EnumerateArray().ShouldAllBe(s => s.GetProperty("state").GetString() == "Pending");
    }

    [Fact]
    public async Task Plans_of_another_owner_are_404_and_anonymous_requests_are_401()
    {
        var client = HoldingClient();
        var owner = await client.RegisterAsync();
        var intruder = await client.RegisterAsync();
        var profileId = await CreateProfileAsync(client, owner.AccessToken);
        var planId = (await (await StartAsync(client, owner.AccessToken, profileId, Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await GetAsync(client, intruder.AccessToken, $"{PlansPath}/{planId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(client, intruder.AccessToken, $"{PlansPath}/{planId}/status")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(PlansPath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/me/quota")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_is_scoped_paged_and_filterable_by_profile_and_status()
    {
        var client = HoldingClient();
        var session = await client.RegisterAsync();
        var other = await client.RegisterAsync();
        var profileA = await CreateProfileAsync(client, session.AccessToken);
        var profileB = await CreateProfileAsync(client, session.AccessToken);
        await StartAsync(client, session.AccessToken, profileA, Guid.NewGuid().ToString());
        await StartAsync(client, session.AccessToken, profileB, Guid.NewGuid().ToString());
        await StartAsync(client, other.AccessToken, await CreateProfileAsync(client, other.AccessToken), Guid.NewGuid().ToString());

        var all = await (await GetAsync(client, session.AccessToken, PlansPath)).Content.ReadFromJsonAsync<JsonElement>();
        var byProfile = await (await GetAsync(client, session.AccessToken, $"{PlansPath}?businessProfileId={profileA}")).Content.ReadFromJsonAsync<JsonElement>();
        var completed = await (await GetAsync(client, session.AccessToken, $"{PlansPath}?status=Completed")).Content.ReadFromJsonAsync<JsonElement>();
        var badPage = await GetAsync(client, session.AccessToken, $"{PlansPath}?pageSize=51");

        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var item = all.GetProperty("items")[0];
        item.GetProperty("brandName").GetString().ShouldBe("Synthetic Eko Threads");
        item.GetProperty("counts").GetProperty("evidence").GetInt32().ShouldBe(0);
        byProfile.GetProperty("totalCount").GetInt32().ShouldBe(1);
        byProfile.GetProperty("items")[0].GetProperty("businessProfileId").GetGuid().ShouldBe(profileA);
        completed.GetProperty("totalCount").GetInt32().ShouldBe(0);
        badPage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Startup_recovery_requeues_Queued_plans_and_fails_interrupted_Running_plans()
    {
        var holding = HoldingClient();
        var session = await holding.RegisterAsync();
        var queuedPlanId = (await (await StartAsync(holding, session.AccessToken, await CreateProfileAsync(holding, session.AccessToken), Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var runningPlanId = (await (await StartAsync(holding, session.AccessToken, await CreateProfileAsync(holding, session.AccessToken), Guid.NewGuid().ToString()))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var running = (await db.GrowthPlans.FindAsync(runningPlanId))!;
            running.Start(new RunContext("1.0", "synth-v1", QlooMode.Mock, LlmProvider.Mock, "mock-v1", true), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        // What the main host's worker does at startup; its real queue then hands the plan to its worker.
        await StuckPlanRecovery.RunAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(), CancellationToken.None);

        var requeued = await WaitForTerminalAsync(_client, session.AccessToken, queuedPlanId);
        requeued.GetProperty("status").GetString().ShouldNotBe("Failed");
        var interrupted = await (await GetAsync(_client, session.AccessToken, $"{PlansPath}/{runningPlanId}/status")).Content.ReadFromJsonAsync<JsonElement>();
        interrupted.GetProperty("status").GetString().ShouldBe("Failed");
        interrupted.GetProperty("error").GetProperty("code").GetString().ShouldBe("timeout");
        interrupted.GetProperty("error").GetProperty("message").GetString().ShouldBe(StuckPlanRecovery.InterruptedMessage);
    }
}
