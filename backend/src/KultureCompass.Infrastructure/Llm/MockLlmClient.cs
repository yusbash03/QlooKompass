using System.Text.Json;
using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Llm;

public enum MockLlmScenario
{
    Valid,
    /// <summary>First synthesis call returns broken JSON; the repair call then succeeds.</summary>
    InvalidJsonOnce,
    /// <summary>Every synthesis/repair output cites ev_999 in one insight (exercises repair exhaustion → drop).</summary>
    UnknownEvidenceKey,
    Truncated,
    QuotaExceeded,
}

/// <summary>Deterministic LLM substitute (BE-04 §2). Builds its output ONLY from the evidence passed in the prompt
/// (first items by quality), every text field prefixed "[MOCK]", so mock plans are schema-valid, grounded and
/// visibly labelled. Plans made with it carry isMock=true.</summary>
public sealed class MockLlmClient(IOptions<LlmOptions> options) : ILlmClient
{
    private static readonly HashSet<string> PartnerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "brand", "artist", "person", "place", "podcast", "tv_show",
    };

    private int _synthesisCalls;

    public string ProviderName => "Mock";

    public string Model => "mock-v1";

    public bool IsMock => true;

    private MockLlmScenario Scenario =>
        Enum.TryParse<MockLlmScenario>(options.Value.MockScenario, ignoreCase: true, out var s) ? s : MockLlmScenario.Valid;

    public Task<LlmResult> GenerateJsonAsync(LlmJsonRequest request, CancellationToken ct)
    {
        var usage = new LlmUsage(request.UserContent.Length / 4, 0);

        if (request.SchemaName == PromptLibrary.InterpretationSchemaName)
            return Task.FromResult(LlmResult.Ok(Interpretation(request.UserContent), usage));
        if (request.SchemaName == PromptLibrary.LlmOnlySchemaName)
            return Task.FromResult(LlmResult.Ok(LlmOnlyDraft(), usage));

        var call = Interlocked.Increment(ref _synthesisCalls);
        LlmResult result = Scenario switch
        {
            MockLlmScenario.QuotaExceeded => LlmResult.Fail(LlmErrorKind.QuotaExceeded, "Mock quota exhausted."),
            MockLlmScenario.Truncated => LlmResult.Fail(LlmErrorKind.Truncated, "Mock output truncated."),
            MockLlmScenario.InvalidJsonOnce when call == 1 => LlmResult.Ok("{\"schemaVersion\": \"1.0\", \"summary\": ", usage),
            _ => LlmResult.Ok(Draft(request.UserContent, Scenario == MockLlmScenario.UnknownEvidenceKey), usage),
        };
        return Task.FromResult(result);
    }

    private static string Interpretation(string userContent)
    {
        var brief = Block(userContent, "brief") as JsonObject;
        return new JsonObject
        {
            ["category"] = brief?["category"]?.GetValue<string>() ?? "Other",
            ["productThemes"] = new JsonArray(),
            ["aestheticKeywords"] = new JsonArray(),
            ["targetCustomerSummary"] = brief?["targetMarket"]?.GetValue<string>() ?? "",
            ["objective"] = brief?["marketingObjective"]?.GetValue<string>() ?? "Awareness",
            ["constraints"] = new JsonArray(),
            ["requestedOutputs"] = new JsonArray("collaborations", "campaigns", "experiments"),
            ["tagSearchTerms"] = new JsonArray((brief?["category"]?.GetValue<string>() ?? "fashion").ToLowerInvariant()),
            ["suggestedSeedNames"] = new JsonArray(), // the mock never proposes entities
            ["country"] = null,
            ["city"] = null,
        }.ToJsonString();
    }

    private static string Draft(string userContent, bool injectUnknownKey)
    {
        var evidence = (Block(userContent, "evidence") as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(e => (Key: e["key"]!.GetValue<string>(), Name: e["name"]!.GetValue<string>(),
                Type: e["type"]?.GetValue<string>() ?? "", Quality: e["quality"]?.GetValue<string>() ?? "Weak"))
            .OrderBy(e => e.Quality switch { "Strong" => 0, "Moderate" => 1, _ => 2 })
            .ToList();
        if (evidence.Count == 0)
            return "{}";

        var constraints = Block(userContent, "constraints") as JsonObject;
        var collaborationsAllowed = constraints?["collaborations"]?.GetValue<string>() != "0";

        string Key(int i) => evidence[i % evidence.Count].Key;

        var insights = Enumerable.Range(0, 3).Select(i => new
        {
            id = $"ins_{i + 1}",
            title = $"[MOCK] Cultural signal around {evidence[i % evidence.Count].Name}",
            finding = $"[MOCK] Retrieved evidence links this brief to {evidence[i % evidence.Count].Name}.",
            whyItMatters = "[MOCK] Template interpretation generated without an LLM.",
            evidenceKeys = new[] { Key(i) }.Concat(injectUnknownKey && i == 0 ? ["ev_999"] : []).ToArray(),
            assumptions = new[] { "[MOCK] Mock output; not a real analysis." },
            limitations = Array.Empty<string>(),
        }).ToList();

        var partners = collaborationsAllowed ? evidence.Where(e => PartnerTypes.Contains(e.Type)).Take(2).ToList() : [];
        var collaborations = partners.Select((p, i) => new
        {
            id = $"col_{i + 1}",
            partnerEvidenceKey = p.Key,
            partnerName = p.Name,
            culturalConnection = $"[MOCK] {p.Name} appears in the retrieved evidence for this brief.",
            format = "[MOCK] Capsule drop",
            fitRationale = "[MOCK] Template rationale.",
            testIdea = "[MOCK] Run a small pre-order test.",
            evidenceKeys = new[] { p.Key },
            assumptions = new[] { "[MOCK] Partner availability is unknown." },
        }).ToList();

        var campaigns = new[]
        {
            new
            {
                id = "cam_1",
                title = "[MOCK] Culture-led content series",
                objective = "[MOCK] Test the brief's objective with a small campaign.",
                targetHypothesis = "[MOCK] The audience responds to the retrieved cultural signals.",
                culturalInsight = $"[MOCK] Built on {evidence[0].Name}.",
                angle = "[MOCK] Culture-first storytelling",
                execution = new[] { "[MOCK] Post three short videos", "[MOCK] Track saves and DMs" },
                successMetric = "Saves per post",
                effortCategory = "Low",
                evidenceKeys = new[] { Key(0) },
                assumptions = new[] { "[MOCK] Mock output; not a real analysis." },
            },
        };

        var experiments = new[]
        {
            new
            {
                id = "exp_1",
                linkedRecommendationId = "cam_1",
                hypothesis = "[MOCK] Culture-first posts get more saves than product-only posts.",
                segment = "[MOCK] Existing followers",
                proposedCampaign = "[MOCK] Culture-led content series",
                primaryMetric = "Saves per post",
                measurementPeriodDays = 14,
                successThreshold = "≥ 20% more saves than the previous 14 days",
                thresholdIsProposedDefault = true,
                ifSuccess = "[MOCK] Continue the series.",
                ifFailure = "[MOCK] Try a different angle.",
            },
        };

        return JsonSerializer.Serialize(new
        {
            schemaVersion = PromptLibrary.SchemaVersion,
            summary = new
            {
                headline = "[MOCK] Sample growth plan from retrieved evidence",
                overview = "[MOCK] This plan was produced by the deterministic mock provider from the retrieved evidence only. It is not an AI analysis.",
            },
            insights,
            collaborations,
            campaigns,
            experiments,
            mentionedEntities = partners.Select(p => new { name = p.Name, evidenceKey = p.Key }),
            limitations = new[] { "[MOCK] Generated by the mock LLM provider." },
        });
    }

    /// <summary>Evaluation-mode baseline: no evidence, so every named entity is declared with a null key.</summary>
    private static string LlmOnlyDraft()
    {
        static object Insight(int n) => new
        {
            id = $"ins_{n}",
            title = $"[MOCK] Ungrounded insight {n}",
            finding = "[MOCK] General-knowledge claim about Synthetic Invented Label.",
            whyItMatters = "[MOCK] Template interpretation.",
            assumptions = new[] { "[MOCK] Not based on retrieved evidence." },
            limitations = Array.Empty<string>(),
        };

        return JsonSerializer.Serialize(new
        {
            schemaVersion = PromptLibrary.SchemaVersion,
            summary = new { headline = "[MOCK] LLM-only baseline plan", overview = "[MOCK] Produced without any retrieved evidence, for comparison in evaluation mode only." },
            insights = new[] { Insight(1), Insight(2), Insight(3) },
            collaborations = new[]
            {
                new
                {
                    id = "col_1", partnerName = "Synthetic Invented Label", culturalConnection = "[MOCK] Assumed overlap.", format = "[MOCK] Capsule drop",
                    fitRationale = "[MOCK] Unverified.", testIdea = "[MOCK] Pre-order test.", assumptions = new[] { "[MOCK] Partner not verified." },
                },
            },
            campaigns = new[]
            {
                new
                {
                    id = "cam_1", title = "[MOCK] Generic content push", objective = "[MOCK] Sales", targetHypothesis = "[MOCK] Audience likes content.",
                    culturalInsight = "[MOCK] Unverified.", angle = "[MOCK] Lifestyle", execution = new[] { "[MOCK] Post", "[MOCK] Track" },
                    successMetric = "Clicks", effortCategory = "Low", assumptions = new[] { "[MOCK] Expect a 40% lift." },
                },
            },
            experiments = new[]
            {
                new
                {
                    id = "exp_1", linkedRecommendationId = "cam_1", hypothesis = "[MOCK] Content drives clicks.", segment = "[MOCK] Followers",
                    proposedCampaign = "[MOCK] Generic content push", primaryMetric = "Clicks", measurementPeriodDays = 14,
                    successThreshold = "≥ 20% more clicks", thresholdIsProposedDefault = true, ifSuccess = "[MOCK] Scale.", ifFailure = "[MOCK] Stop.",
                },
            },
            mentionedEntities = new[] { new { name = "Synthetic Invented Label", evidenceKey = (string?)null } },
            limitations = new[] { "[MOCK] LLM-only output; nothing here was retrieved." },
        });
    }

    /// <summary>Parses the JSON inside the first <c>&lt;tag&gt;…&lt;/tag&gt;</c> block of the prompt.</summary>
    private static JsonNode? Block(string content, string tag)
    {
        var open = content.IndexOf($"<{tag}>", StringComparison.Ordinal);
        var close = content.IndexOf($"</{tag}>", StringComparison.Ordinal);
        if (open < 0 || close < open)
            return null;
        try
        {
            return JsonNode.Parse(content[(open + tag.Length + 2)..close]);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
