using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KultureCompass.Application.Plans;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KultureCompass.Api.Tests;

[Collection(ApiCollection.Name)]
public class EvaluationAndSampleTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateApiClient();

    private HttpClient ClientWithFlags(bool evaluation, bool samplePlan, bool experiments) => factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Generation:RecoverOnStartup", "false");
            b.UseSetting("Features:EvaluationMode", evaluation.ToString());
            b.UseSetting("Features:SamplePlan", samplePlan.ToString());
            b.UseSetting("Features:ExperimentTracking", experiments.ToString());
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token, object? body = null,
        string? idempotencyKey = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url).WithBearer(token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> PollAsync(HttpClient client, string token, string url, Func<JsonElement, bool> done)
    {
        for (var i = 0; i < 150; i++)
        {
            var body = await (await SendAsync(client, HttpMethod.Get, url, token)).Content.ReadFromJsonAsync<JsonElement>();
            if (done(body))
                return body;
            await Task.Delay(200);
        }
        throw new TimeoutException(url);
    }

    private static bool IsTerminal(JsonElement e) => e.GetProperty("status").GetString() is "Completed" or "CompletedWithLimitations" or "Failed";

    private async Task<(TestSession Session, Guid PlanId)> CompletedPlanAsync()
    {
        var session = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, session.AccessToken);
        var start = await SendAsync(_client, HttpMethod.Post, "/api/v1/growth-plans", session.AccessToken,
            new { businessProfileId = profileId }, Guid.NewGuid().ToString());
        var planId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await PollAsync(_client, session.AccessToken, $"/api/v1/growth-plans/{planId}/status", IsTerminal);
        return (session, planId);
    }

    // ── Evaluation mode ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluation_runs_grounded_and_llm_only_and_scores_both_deterministically()
    {
        var session = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, session.AccessToken);
        var key = Guid.NewGuid().ToString();

        var start = await SendAsync(_client, HttpMethod.Post, "/api/v1/evaluations", session.AccessToken, new { businessProfileId = profileId }, key);

        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var accepted = await start.Content.ReadFromJsonAsync<JsonElement>();
        var evaluationId = accepted.GetProperty("id").GetGuid();
        start.Headers.Location!.OriginalString.ShouldBe($"/api/v1/evaluations/{evaluationId}");
        var groundedPlanId = accepted.GetProperty("groundedPlanId").GetGuid();

        var evaluation = await PollAsync(_client, session.AccessToken, $"/api/v1/evaluations/{evaluationId}",
            e => e.GetProperty("status").GetString() is "Completed" or "Failed");

        evaluation.GetProperty("status").GetString().ShouldBe("Completed", evaluation.ToString());
        var grounded = evaluation.GetProperty("groundedMetrics");
        var llmOnly = evaluation.GetProperty("llmOnlyMetrics");
        grounded.GetProperty("traceableEntities").GetInt32().ShouldBeGreaterThan(0);
        grounded.GetProperty("recommendationsWithEvidencePct").GetDouble().ShouldBe(100);
        llmOnly.GetProperty("traceableEntities").GetInt32().ShouldBe(0);
        llmOnly.GetProperty("unsupportedEntities").GetInt32().ShouldBeGreaterThan(0);
        llmOnly.GetProperty("recommendationsWithEvidencePct").ValueKind.ShouldBe(JsonValueKind.Null);
        evaluation.GetProperty("llmOnlyOutput").GetProperty("insights").GetArrayLength().ShouldBe(3);
        evaluation.GetProperty("notes").EnumerateArray().ShouldContain(n => n.GetString()!.Contains("No winner is declared"));

        // Replay of the same evaluation request → same evaluation; the grounded plan is a normal, readable plan.
        var replay = await SendAsync(_client, HttpMethod.Post, "/api/v1/evaluations", session.AccessToken, new { businessProfileId = profileId }, key);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ShouldBe(evaluationId);
        (await SendAsync(_client, HttpMethod.Get, $"/api/v1/growth-plans/{groundedPlanId}", session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Evaluation_of_another_owner_is_404_and_missing_key_is_400()
    {
        var owner = await _client.RegisterAsync();
        var intruder = await _client.RegisterAsync();
        var profileId = await CreateProfileAsync(_client, owner.AccessToken);
        var start = await SendAsync(_client, HttpMethod.Post, "/api/v1/evaluations", owner.AccessToken, new { businessProfileId = profileId }, Guid.NewGuid().ToString());
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await SendAsync(_client, HttpMethod.Get, $"/api/v1/evaluations/{id}", intruder.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var noKey = await SendAsync(_client, HttpMethod.Post, "/api/v1/evaluations", owner.AccessToken, new { businessProfileId = profileId });
        (await noKey.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("idempotency_key_missing");
    }

    [Fact]
    public async Task Disabled_flags_return_503_feature_disabled()
    {
        var client = ClientWithFlags(evaluation: false, samplePlan: false, experiments: false);
        var session = await client.RegisterAsync();

        var responses = new[]
        {
            await SendAsync(client, HttpMethod.Post, "/api/v1/evaluations", session.AccessToken, new { businessProfileId = Guid.NewGuid() }, Guid.NewGuid().ToString()),
            await SendAsync(client, HttpMethod.Get, $"/api/v1/evaluations/{Guid.NewGuid()}", session.AccessToken),
            await client.GetAsync("/api/v1/sample-plan"),
            await SendAsync(client, HttpMethod.Patch, $"/api/v1/growth-plans/{Guid.NewGuid()}/experiments/exp_1", session.AccessToken, new { status = "Running" }, ifMatch: "\"1\""),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("feature_disabled");
        }
    }

    // ── Sample plan ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sample_plan_is_public_labelled_synthetic_grounded_and_matches_the_plan_contract()
    {
        var response = await _client.GetAsync("/api/v1/sample-plan");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var plan = JsonSerializer.Deserialize<GrowthPlanDto>(raw, ApiFactory.Json)!; // shape check against the real DTO
        plan.IsMock.ShouldBeTrue();
        plan.IsSample.ShouldBeTrue();
        plan.Evidence.ShouldAllBe(e => e.Name.StartsWith("Synthetic "));
        plan.BriefSnapshot.BrandName.ShouldStartWith("Synthetic ");
        var keys = plan.Evidence.Select(e => e.Key).ToHashSet();
        plan.Insights.SelectMany(i => i.EvidenceKeys).Concat(plan.Collaborations.SelectMany(c => c.EvidenceKeys))
            .Concat(plan.Campaigns.SelectMany(c => c.EvidenceKeys)).ShouldAllBe(k => keys.Contains(k));
        plan.Collaborations.ShouldAllBe(c => plan.Evidence.Any(e => e.Key == c.PartnerEvidenceKey && e.Name == c.PartnerName));
        var recIds = plan.Insights.Select(i => i.Id).Concat(plan.Campaigns.Select(c => c.Id)).Concat(plan.Collaborations.Select(c => c.Id)).ToHashSet();
        plan.Experiments.ShouldAllBe(e => recIds.Contains(e.LinkedRecommendationId) && e.ThresholdIsProposedDefault);
    }

    // ── Experiment tracking ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Experiment_tracking_enforces_If_Match_and_only_changes_tracking_fields()
    {
        var (session, planId) = await CompletedPlanAsync();
        var plan = await (await SendAsync(_client, HttpMethod.Get, $"/api/v1/growth-plans/{planId}", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var experiment = plan.GetProperty("experiments")[0];
        var expId = experiment.GetProperty("id").GetString();
        var version = experiment.GetProperty("version").GetString();
        var url = $"/api/v1/growth-plans/{planId}/experiments/{expId}";
        var body = new { status = "Running", observedResult = "Synthetic: 12 pre-orders in week 1", resultNotes = "Synthetic note" };

        var noIfMatch = await SendAsync(_client, HttpMethod.Patch, url, session.AccessToken, body);
        noIfMatch.StatusCode.ShouldBe((HttpStatusCode)428);

        var ok = await SendAsync(_client, HttpMethod.Patch, url, session.AccessToken, body, ifMatch: $"\"{version}\"");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ok.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("status").GetString().ShouldBe("Running");
        updated.GetProperty("observedResult").GetString().ShouldBe("Synthetic: 12 pre-orders in week 1");
        updated.GetProperty("hypothesis").GetString().ShouldBe(experiment.GetProperty("hypothesis").GetString());
        experiment.GetProperty("startedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        updated.GetProperty("startedAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
        updated.GetProperty("concludedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        ok.Headers.ETag!.Tag.ShouldNotBe($"\"{version}\"");

        var stale = await SendAsync(_client, HttpMethod.Patch, url, session.AccessToken, body, ifMatch: $"\"{version}\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("version_mismatch");

        var tooLong = await SendAsync(_client, HttpMethod.Patch, url, session.AccessToken,
            new { status = "Concluded", observedResult = new string('x', 201) }, ifMatch: ok.Headers.ETag.Tag);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Experiments_hub_lists_the_callers_experiments_across_plans_with_status_filter()
    {
        var (session, planId) = await CompletedPlanAsync();
        var otherProfile = await CreateProfileAsync(_client, session.AccessToken);
        var second = await SendAsync(_client, HttpMethod.Post, "/api/v1/growth-plans", session.AccessToken,
            new { businessProfileId = otherProfile }, Guid.NewGuid().ToString());
        var secondPlanId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await PollAsync(_client, session.AccessToken, $"/api/v1/growth-plans/{secondPlanId}/status", IsTerminal);
        await CompletedPlanAsync(); // someone else's plan must not appear

        var plan = await (await SendAsync(_client, HttpMethod.Get, $"/api/v1/growth-plans/{planId}", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var exp = plan.GetProperty("experiments")[0];
        await SendAsync(_client, HttpMethod.Patch, $"/api/v1/growth-plans/{planId}/experiments/{exp.GetProperty("id").GetString()}",
            session.AccessToken, new { status = "Running" }, ifMatch: $"\"{exp.GetProperty("version").GetString()}\"");

        var all = await (await SendAsync(_client, HttpMethod.Get, "/api/v1/experiments", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var running = await (await SendAsync(_client, HttpMethod.Get, "/api/v1/experiments?status=Running", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();
        var paged = await (await SendAsync(_client, HttpMethod.Get, "/api/v1/experiments?pageSize=1", session.AccessToken)).Content.ReadFromJsonAsync<JsonElement>();

        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("growthPlanId").GetGuid()).ToHashSet().ShouldBe([planId, secondPlanId], ignoreOrder: true);
        items[0].GetProperty("growthPlanId").GetGuid().ShouldBe(secondPlanId); // newest plan first
        items.ShouldAllBe(i => i.GetProperty("brandName").GetString() == "Synthetic Eko Threads");
        running.GetProperty("totalCount").GetInt32().ShouldBe(1);
        running.GetProperty("items")[0].GetProperty("experiment").GetProperty("startedAt").ValueKind.ShouldBe(JsonValueKind.String);
        paged.GetProperty("items").GetArrayLength().ShouldBe(1);
        paged.GetProperty("totalCount").GetInt32().ShouldBe(2);
        (await _client.GetAsync("/api/v1/experiments")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Experiment_tracking_is_404_for_unknown_experiment_or_another_owner()
    {
        var (session, planId) = await CompletedPlanAsync();
        var intruder = await _client.RegisterAsync();

        (await SendAsync(_client, HttpMethod.Patch, $"/api/v1/growth-plans/{planId}/experiments/exp_9", session.AccessToken,
            new { status = "Running" }, ifMatch: "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(_client, HttpMethod.Patch, $"/api/v1/growth-plans/{planId}/experiments/exp_1", intruder.AccessToken,
            new { status = "Running" }, ifMatch: "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
