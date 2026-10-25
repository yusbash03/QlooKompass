using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Domain.Recommendations;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Llm;
using KultureCompass.Infrastructure.Qloo;
using KultureCompass.Infrastructure.Tests.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit.Abstractions;
using QlooMode = KultureCompass.Infrastructure.Qloo.QlooMode;

namespace KultureCompass.Infrastructure.Tests.Llm;

/// <summary>Real Gemini (and, for the pipeline test, real Qloo). Run with
/// <c>LLM_API_KEY=… QLOO_API_KEY=… dotnet test --filter "Category=Live"</c>. Output is written to the test log only:
/// counts, model ids and token usage — nothing from these runs is committed.</summary>
[Trait("Category", "Live")]
[Collection(PostgresCollection.Name)]
public class LlmLiveTests(PostgresFixture fixture, ITestOutputHelper output)
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static string? LlmKey => Environment.GetEnvironmentVariable("LLM_API_KEY");
    private static string? QlooKey => Environment.GetEnvironmentVariable("QLOO_API_KEY");
    private static string Model => Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gemini-flash-lite-latest";

    private static GeminiLlmClient Gemini() => new(
        new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
        Options.Create(new LlmOptions { Provider = "Gemini", ApiKey = LlmKey, Model = Model }),
        NullLogger<GeminiLlmClient>.Instance);

    [Fact]
    public async Task Gemini_tiny_schema_call_returns_json_and_usage()
    {
        if (string.IsNullOrEmpty(LlmKey))
            return;

        var schema = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["ok"],"properties":{"ok":{"type":"boolean"}}}""")!;
        var result = await Gemini().GenerateJsonAsync(new LlmJsonRequest("Return only JSON.", "Say ok=true.", schema, "live-tiny", 200), CancellationToken.None);

        var success = result.ShouldBeOfType<LlmResult.Success>();
        JsonNode.Parse(success.Json)!["ok"]!.GetValue<bool>().ShouldBeTrue();
        output.WriteLine($"model={Model} usage={success.Usage}");
    }

    [Fact]
    public async Task Gemini_growth_plan_schema_output_passes_the_validator_on_synthetic_evidence()
    {
        if (string.IsNullOrEmpty(LlmKey))
            return;

        var llm = Gemini();
        var evidence = Enumerable.Range(1, 8).Select(i => Domain.Evidence.EvidenceItem.Create(
            $"ev_{i:000}", $"synthetic-{i}", $"Synthetic Entity {i}", i % 3 == 0 ? "urn:tag:keyword:qloo" : i % 2 == 0 ? "urn:entity:brand" : "urn:entity:artist",
            null, Domain.Evidence.SourceOperation.Insights, ["Synthetic Seed"], 0.9 - i * 0.05, 0.5, ["streetwear"],
            Domain.Evidence.EvidenceMetadata.Empty, Domain.Evidence.EvidenceQuality.Strong, ["resolved seed signal"], 1, DateTimeOffset.UtcNow)).ToList();
        var snapshot = PipelineTestHost.LagosStreetwearBrief();
        var input = new SynthesisInput(snapshot, Application.Research.BriefInterpretation.DeriveDeterministically(snapshot), evidence, [], GenerationOptions.Default);
        var prompt = RecommendationSynthesizer.BuildPrompt(input);
        var synthesizer = new RecommendationSynthesizer(llm);

        var attempt = await synthesizer.GenerateAsync(prompt, 6000, CancellationToken.None);
        output.WriteLine($"model={Model} call={attempt.Call}");
        RecommendationSynthesizer.FatalError(attempt.Result).ShouldBeNull();
        var validated = await synthesizer.ValidateAndRepairAsync(input, prompt, attempt.Result, 6000, 2, CancellationToken.None);

        output.WriteLine($"repairs={validated.RepairCalls.Count} limitations={validated.Limitations.Count} error={validated.Error}");
        validated.Error.ShouldBeNull();
        output.WriteLine($"insights={validated.Draft!.Insights!.Count} collaborations={validated.Draft.Collaborations!.Count} " +
                         $"campaigns={validated.Draft.Campaigns!.Count} experiments={validated.Draft.Experiments!.Count} unlinked={validated.UnlinkedMentions.Count}");
    }

    [Fact]
    public async Task Gemini_accepts_the_llm_only_schema_and_declares_entities_without_keys()
    {
        if (string.IsNullOrEmpty(LlmKey))
            return;

        var brief = PipelineTestHost.LagosStreetwearBrief();
        var user = $"<brief>{System.Text.Json.JsonSerializer.Serialize(new { brief.BrandName, brief.Category, brief.ProductsDescription, brief.PrimaryLocation, brief.MarketingObjective }, WebJson)}</brief>" +
                   "<constraints>{\"insights\":\"3-6\",\"collaborations\":\"0-4\",\"campaigns\":\"1-3\"}</constraints>";

        var result = await Gemini().GenerateJsonAsync(new LlmJsonRequest(
            PromptLibrary.LlmOnlySystem, user, PromptLibrary.LlmOnlySchema(), PromptLibrary.LlmOnlySchemaName, 6000), CancellationToken.None);

        var success = result.ShouldBeOfType<LlmResult.Success>();
        var draft = System.Text.Json.JsonSerializer.Deserialize<GrowthPlanDraft>(success.Json, WebJson)!;
        output.WriteLine($"usage={success.Usage} insights={draft.Insights!.Count} collabs={draft.Collaborations!.Count} campaigns={draft.Campaigns!.Count} mentioned={draft.MentionedEntities!.Count}");
        draft.Insights.Count.ShouldBeGreaterThanOrEqualTo(3);
        draft.MentionedEntities.ShouldAllBe(m => m.EvidenceKey == null);
        success.Json.ShouldNotContain("ev_0");
    }

    [Fact]
    public async Task Full_pipeline_with_live_Qloo_and_live_Gemini_produces_a_grounded_plan()
    {
        if (string.IsNullOrEmpty(LlmKey) || string.IsNullOrEmpty(QlooKey))
            return;

        // Same public reference names as the committed backend/samples/lagos-streetwear-sample.json.
        var brief = PipelineTestHost.LagosStreetwearBrief([
            ReferenceEntity.Create("Burna Boy", ReferenceKindHint.Artist),
            ReferenceEntity.Create("Wizkid", ReferenceKindHint.Artist),
            ReferenceEntity.Create("Orange Culture", ReferenceKindHint.Brand),
        ]);
        var planId = await PipelineTestHost.CreateQueuedPlanAsync(fixture, brief);

        await PipelineTestHost.RunAsync(fixture, planId, LiveQloo(), Gemini(), Domain.Plans.QlooMode.Live);

        var plan = await PipelineTestHost.ReloadAsync(fixture, planId);
        output.WriteLine($"status={plan.Status} error={plan.Error} model={plan.LlmModel} llmCalls={plan.LlmCallCount} qlooCalls={plan.QlooCallCount}");
        output.WriteLine($"evidence={plan.Evidence.Count} recs={string.Join(",", plan.Recommendations.GroupBy(r => r.Kind).Select(g => $"{g.Key}:{g.Count()}"))} " +
                         $"experiments={plan.Experiments.Count} unlinked={plan.UnlinkedMentions.Count} limitations={plan.Limitations.Count}");
        foreach (var limitation in plan.Limitations)
            output.WriteLine($"limitation: {limitation}");
        output.WriteLine($"usage={plan.LlmUsageJson}");
        output.WriteLine(string.Join(" | ", plan.StageLog.Select(s => $"{s.Stage}:{s.State}:{s.Detail}")));

        plan.Status.ShouldBeOneOf(GenerationStatus.Completed, GenerationStatus.CompletedWithLimitations);
        plan.IsMock.ShouldBeFalse();
        var keys = plan.Evidence.Select(e => e.Key).ToHashSet();
        plan.Recommendations.SelectMany(r => r.EvidenceKeys).ShouldAllBe(k => keys.Contains(k));
        plan.Recommendations.Where(r => r.Kind == RecommendationKind.Collaboration)
            .ShouldAllBe(r => plan.Evidence.Any(e => e.Name == ((CollaborationPayload)r.Payload).PartnerName));
    }

    private static IQlooClient LiveQloo()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new QlooOptions
        {
            BaseUrl = "https://hackathon.api.qloo.com", ApiKey = QlooKey, Mode = QlooMode.Live,
        }));
        services.AddSingleton<QlooApiKeyHandler>();
        services.AddSingleton<QlooThrottleHandler>();
        services.AddSingleton<RedactingLoggingHandler>();
        services.AddHttpClient<IQlooClient, QlooClient>(c => c.BaseAddress = new Uri("https://hackathon.api.qloo.com"))
            .AddHttpMessageHandler<QlooApiKeyHandler>()
            .AddHttpMessageHandler<QlooThrottleHandler>()
            .AddHttpMessageHandler<RedactingLoggingHandler>()
            .AddStandardResilienceHandler(r => QlooServiceCollectionExtensions.ConfigureResilience(r, TimeSpan.FromSeconds(20)));
        return services.BuildServiceProvider().GetRequiredService<IQlooClient>();
    }
}
