using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Tests.Generation;

internal static class GenerationTestData
{
    public static readonly DateTimeOffset RetrievedAt = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static BriefSnapshot Snapshot() => new(
        "Synthetic Eko Threads", "A synthetic Lagos streetwear label", BusinessCategory.Streetwear, null,
        "Graphic tees and caps", "Lagos 18-30", "Lagos, Nigeria", MarketingObjective.Sales, null, null,
        [ReferenceEntity.Create("Synthetic Afrobeat Star", ReferenceKindHint.Artist)], BudgetRange.From100kTo500k);

    /// <summary>ev_001 artist, ev_002 brand, ev_003 tag, ev_004 place, ev_005 brand, ev_006 podcast.</summary>
    public static IReadOnlyList<EvidenceItem> Evidence(int count = 6)
    {
        (string Name, string Type)[] specs =
        [
            ("Synthetic Afrobeat Star", "urn:entity:artist"),
            ("Synthetic Lagos Streetwear Co", "urn:entity:brand"),
            ("Synthetic Street Culture", "urn:tag:keyword:qloo"),
            ("Synthetic Lekki Lounge", "urn:entity:place"),
            ("Synthetic Danfo Denim", "urn:entity:brand"),
            ("Synthetic Alte Podcast", "urn:entity:podcast"),
        ];

        return [.. specs.Take(count).Select((s, i) => EvidenceItem.Create(
            $"ev_{i + 1:000}", $"synthetic-{i + 1}", s.Name, s.Type, null, SourceOperation.Insights, ["Synthetic Afrobeat Star"],
            0.9 - i * 0.05, 0.6, ["streetwear"], EvidenceMetadata.Empty, EvidenceQuality.Strong, ["resolved seed signal"], 1, RetrievedAt))];
    }

    public static EvidenceSet EvidenceSet(int count = 6, params string[] limitations) =>
        new(Evidence(count), limitations, CallsMade: 4, OperationsExecuted: ["R1", "R2", "R3", "R4"]);

    /// <summary>A draft that passes every validator rule against <see cref="Evidence"/>.</summary>
    public static JsonObject ValidDraft(int insights = 3) => new()
    {
        ["schemaVersion"] = "1.0",
        ["summary"] = new JsonObject
        {
            ["headline"] = "Ground the brand in Lagos alté culture",
            ["overview"] = "Retrieved evidence links the brand's audience to alté music and streetwear labels in Lagos.",
        },
        ["insights"] = new JsonArray([.. Enumerable.Range(1, insights).Select(n => (JsonNode)new JsonObject
        {
            ["id"] = $"ins_{n}",
            ["title"] = $"Insight {n}",
            ["finding"] = "The evidence shows a strong taste association.",
            ["whyItMatters"] = "This suggests content angles for the brand.",
            ["evidenceKeys"] = new JsonArray($"ev_00{n}"),
            ["assumptions"] = new JsonArray("Audience overlap is directional."),
            ["limitations"] = new JsonArray(),
        })]),
        ["collaborations"] = new JsonArray(new JsonObject
        {
            ["id"] = "col_1",
            ["partnerEvidenceKey"] = "ev_002",
            ["partnerName"] = "Synthetic Lagos Streetwear Co",
            ["culturalConnection"] = "Shared streetwear audience.",
            ["format"] = "Capsule drop",
            ["fitRationale"] = "High affinity with the seed artist.",
            ["testIdea"] = "Pre-order 30 units.",
            ["evidenceKeys"] = new JsonArray("ev_002"),
            ["assumptions"] = new JsonArray("Partner is open to collaborations."),
        }),
        ["campaigns"] = new JsonArray(new JsonObject
        {
            ["id"] = "cam_1",
            ["title"] = "Alté nights content series",
            ["objective"] = "Drive sales",
            ["targetHypothesis"] = "Fans of the seed artist respond to music-led content.",
            ["culturalInsight"] = "Music and streetwear overlap.",
            ["angle"] = "Behind-the-scenes music culture",
            ["execution"] = new JsonArray("Film three reels", "Post weekly"),
            ["successMetric"] = "Link clicks",
            ["effortCategory"] = "Low",
            ["evidenceKeys"] = new JsonArray("ev_001", "ev_004"),
            ["assumptions"] = new JsonArray(),
        }),
        ["experiments"] = new JsonArray(new JsonObject
        {
            ["id"] = "exp_1",
            ["linkedRecommendationId"] = "cam_1",
            ["hypothesis"] = "Music-led reels get more clicks.",
            ["segment"] = "Followers 18-30",
            ["proposedCampaign"] = "Alté nights content series",
            ["primaryMetric"] = "Link clicks",
            ["measurementPeriodDays"] = 14,
            ["successThreshold"] = "≥ 20% more clicks",
            ["thresholdIsProposedDefault"] = true,
            ["ifSuccess"] = "Scale up.",
            ["ifFailure"] = "Try a new angle.",
        }),
        ["mentionedEntities"] = new JsonArray(new JsonObject { ["name"] = "Synthetic Lagos Streetwear Co", ["evidenceKey"] = "ev_002" }),
        ["limitations"] = new JsonArray(),
    };

    public static OutputValidationResult Validate(JsonNode draft, DraftLimits? limits = null, string grounding = "") =>
        OutputValidator.Validate(draft.ToJsonString(), Evidence(), limits ?? new DraftLimits(), grounding);
}

/// <summary>Returns scripted results per call; interpretation calls are answered separately so tests can script only
/// the synthesis/repair sequence. The last scripted synthesis result repeats once the queue is drained.</summary>
internal sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Queue<LlmResult> _synthesis = new();

    public LlmResult Interpretation { get; set; } = LlmResult.Fail(LlmErrorKind.Unavailable, "not scripted");

    public List<LlmJsonRequest> Requests { get; } = [];

    public string ProviderName => "Gemini";
    public string Model => "scripted-model";
    public bool IsMock => false;

    public ScriptedLlmClient ThenReturn(JsonNode draft) => ThenReturn(LlmResult.Ok(draft.ToJsonString(), new LlmUsage(100, 50)));

    public ScriptedLlmClient ThenReturn(LlmResult result)
    {
        _synthesis.Enqueue(result);
        return this;
    }

    public Task<LlmResult> GenerateJsonAsync(LlmJsonRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (request.SchemaName == PromptLibrary.InterpretationSchemaName)
            return Task.FromResult(Interpretation);
        var result = _synthesis.Count > 1 ? _synthesis.Dequeue() : _synthesis.Peek();
        return Task.FromResult(result);
    }

    public int SynthesisCallCount => Requests.Count(r => r.SchemaName == PromptLibrary.GrowthPlanSchemaName);
}

internal sealed class FakeResearchService : ICulturalIntelligenceService
{
    public Func<CancellationToken, Task<EvidenceSet>> OnRetrieve { get; set; } = _ => Task.FromResult(GenerationTestData.EvidenceSet());

    public async Task<EvidenceSet> RetrieveAsync(ResearchBrief brief, ResearchBudget budget, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("1 of 2 Qloo requests complete");
        progress?.Report("2 of 2 Qloo requests complete");
        return await OnRetrieve(ct);
    }
}

/// <summary>Single-plan store that records the plan's stage at every save, so tests can prove stage_log is persisted
/// after each stage.</summary>
internal sealed class InMemoryGrowthPlanStore(GrowthPlan plan) : IGrowthPlanStore
{
    public GrowthPlan Plan { get; } = plan;

    public List<(GenerationStage Stage, GenerationStatus Status)> Saves { get; } = [];

    public Task<GrowthPlan?> GetForProcessingAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<GrowthPlan?>(id == Plan.Id ? Plan : null);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        lock (Saves)
            Saves.Add((Plan.Stage, Plan.Status));
        return Task.CompletedTask;
    }

    public Task AddAsync(GrowthPlan plan, CancellationToken ct) => throw new NotSupportedException();
    public Task<GrowthPlan?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct) => throw new NotSupportedException();
    public Task<GrowthPlan?> GetByIdempotencyKeyAsync(string ownerId, string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasActivePlanForProfileAsync(Guid businessProfileId, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountCreatedSinceAsync(string ownerId, DateTimeOffset sinceUtc, CancellationToken ct) => throw new NotSupportedException();
    public Task<(IReadOnlyList<GrowthPlan> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, Guid? businessProfileId, GenerationStatus? status, CancellationToken ct) =>
        throw new NotSupportedException();
}
