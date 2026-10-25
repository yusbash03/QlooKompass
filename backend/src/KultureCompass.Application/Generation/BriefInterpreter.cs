using System.Text.Json;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Generation;

public sealed record LlmCallRecord(string Purpose, bool Succeeded, LlmUsage? Usage, string? FailureKind = null);

public sealed record InterpretationOutcome(BriefInterpretation Interpretation, string? Limitation, LlmCallRecord Call);

/// <summary>Call A (BE-04 §1 step 1). Never fails the plan: any LLM or validation problem falls back to
/// <see cref="BriefInterpretation.DeriveDeterministically"/> and records a limitation. User-stated facts
/// (category, objective, location) always win over the model's reading.</summary>
public sealed class BriefInterpreter(ILlmClient llm)
{
    public const string FallbackLimitation =
        "The AI brief-interpretation step was unavailable, so research used simplified search terms and no AI-suggested reference brands.";

    private const int MaxOutputTokens = 1200;

    public async Task<InterpretationOutcome> InterpretAsync(BriefSnapshot snapshot, CancellationToken ct)
    {
        var fallback = BriefInterpretation.DeriveDeterministically(snapshot);

        var result = await llm.GenerateJsonAsync(new LlmJsonRequest(
            PromptLibrary.InterpretSystem,
            $"<brief>{PromptJson.Brief(snapshot)}</brief>",
            PromptLibrary.InterpretationSchema(),
            PromptLibrary.InterpretationSchemaName,
            MaxOutputTokens,
            Temperature: 0.2), ct).ConfigureAwait(false);

        if (result is LlmResult.Failure failure)
            return new(fallback, FallbackLimitation, new("interpret", false, null, failure.Kind.ToString()));

        var success = (LlmResult.Success)result;
        var interpreted = TryMap(success.Json, snapshot, fallback);
        return interpreted is null
            ? new(fallback, FallbackLimitation, new("interpret", false, success.Usage, "InvalidOutput"))
            : new(interpreted, null, new("interpret", true, success.Usage));
    }

    internal static BriefInterpretation? TryMap(string json, BriefSnapshot snapshot, BriefInterpretation fallback)
    {
        InterpretationDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<InterpretationDraft>(json, GrowthPlanDraft.Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (draft is null)
            return null;

        var userReferences = snapshot.ReferenceEntities.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seeds = (draft.SuggestedSeedNames ?? [])
            .Select(s => (Name: CleanTerm(s.Name, 100), s.KindHint))
            .Where(s => s.Name is not null && !userReferences.Contains(s.Name))
            .DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(s => new SuggestedSeedName(s.Name!, Enum.TryParse<ReferenceKindHint>(s.KindHint, out var hint) ? hint : ReferenceKindHint.Unknown))
            .ToList();

        var tagTerms = CleanList(draft.TagSearchTerms, 3, 40).Select(t => t.ToLowerInvariant()).Distinct().ToList();

        return fallback with
        {
            // Category, objective and the user's own location are facts from the form, not model opinions.
            ProductThemes = CleanList(draft.ProductThemes, 5, 60),
            AestheticKeywords = CleanList(draft.AestheticKeywords, 5, 60),
            TargetCustomerSummary = OutputValidator.Clean(draft.TargetCustomerSummary, 300) is { Length: > 0 } summary
                ? summary
                : fallback.TargetCustomerSummary,
            Constraints = CleanList(draft.Constraints, 5, 120),
            TagSearchTerms = tagTerms.Count > 0 ? tagTerms : fallback.TagSearchTerms,
            SuggestedSeedNames = seeds,
            Country = fallback.Country ?? CleanTerm(draft.Country, 60),
            City = fallback.City ?? CleanTerm(draft.City, 60),
        };
    }

    private static List<string> CleanList(List<string>? values, int maxItems, int maxLength) =>
        [.. (values ?? []).Select(v => CleanTerm(v, maxLength)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(maxItems)];

    /// <summary>BE-04: "no URLs" — a term containing one is dropped, not repaired.</summary>
    private static string? CleanTerm(string? value, int maxLength)
    {
        var cleaned = OutputValidator.Clean(value, maxLength);
        if (string.IsNullOrEmpty(cleaned))
            return null;
        return cleaned.Contains("http", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("www.", StringComparison.OrdinalIgnoreCase)
            ? null
            : cleaned;
    }

    private sealed class InterpretationDraft
    {
        public List<string>? ProductThemes { get; set; }
        public List<string>? AestheticKeywords { get; set; }
        public string? TargetCustomerSummary { get; set; }
        public List<string>? Constraints { get; set; }
        public List<string>? TagSearchTerms { get; set; }
        public List<SeedDraft>? SuggestedSeedNames { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
    }

    private sealed class SeedDraft
    {
        public string? Name { get; set; }
        public string? KindHint { get; set; }
    }
}
