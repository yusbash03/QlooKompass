using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Research;

/// <summary>AI-proposed seed name awaiting Qloo resolution in R1 (BE-03 §4). Capped at 3 by
/// <see cref="BriefInterpretation"/>'s producer.</summary>
public sealed record SuggestedSeedName(string Name, ReferenceKindHint KindHint);

/// <summary>Output of <c>BriefInterpreter</c> (call A, BE-04 §1 step 1) — the LLM-proposed reading of a brief.
/// This is the real contract BE-6's <c>BriefInterpreter</c> will produce; BE-4's <see cref="ResearchPlanner"/>
/// consumes it today via <see cref="DeriveDeterministically"/>, the same no-LLM fallback BE-04 §1 step 1
/// specifies for when call A fails ("derive deterministically ... and add a limitation — don't fail the plan").</summary>
public sealed record BriefInterpretation(
    BusinessCategory Category,
    IReadOnlyList<string> ProductThemes,
    IReadOnlyList<string> AestheticKeywords,
    string TargetCustomerSummary,
    MarketingObjective Objective,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> RequestedOutputs,
    IReadOnlyList<string> TagSearchTerms,
    IReadOnlyList<SuggestedSeedName> SuggestedSeedNames,
    string? Country,
    string? City)
{
    /// <summary>Deterministic, LLM-free interpretation: category + objective become tag search terms, no seeds are
    /// suggested. City/country are parsed from the free-text <c>PrimaryLocation</c> ("Lagos, Nigeria") only when it
    /// contains a comma — a single-word location is kept as the city and the country is left unknown rather than
    /// guessed, so the R7 location guard never verifies against a fabricated country.</summary>
    public static BriefInterpretation DeriveDeterministically(BriefSnapshot snapshot)
    {
        var categoryTerm = CategoryTagTerm(snapshot.Category, snapshot.CategoryOther);
        var objectiveTerm = ObjectiveTagTerm(snapshot.MarketingObjective);
        var tagSearchTerms = new List<string> { categoryTerm };
        if (!string.Equals(categoryTerm, objectiveTerm, StringComparison.OrdinalIgnoreCase))
            tagSearchTerms.Add(objectiveTerm);

        var (city, country) = ParseLocation(snapshot.PrimaryLocation);

        return new BriefInterpretation(
            Category: snapshot.Category,
            ProductThemes: [],
            AestheticKeywords: [],
            TargetCustomerSummary: snapshot.TargetMarket ?? string.Empty,
            Objective: snapshot.MarketingObjective,
            Constraints: [],
            RequestedOutputs: ["collaborations", "campaigns", "experiments"],
            TagSearchTerms: tagSearchTerms,
            SuggestedSeedNames: [],
            Country: country,
            City: city);
    }

    private static string CategoryTagTerm(BusinessCategory category, string? categoryOther) => category switch
    {
        BusinessCategory.Streetwear => "streetwear",
        BusinessCategory.Fashion => "fashion",
        BusinessCategory.Lifestyle => "lifestyle",
        BusinessCategory.Beauty => "beauty",
        BusinessCategory.FootwearAccessories => "footwear",
        BusinessCategory.Other => string.IsNullOrWhiteSpace(categoryOther) ? "fashion" : categoryOther.Trim().ToLowerInvariant(),
        _ => "fashion",
    };

    private static string ObjectiveTagTerm(MarketingObjective objective) => objective switch
    {
        MarketingObjective.Awareness => "awareness",
        MarketingObjective.Engagement => "engagement",
        MarketingObjective.Sales => "sales",
        MarketingObjective.Launch => "launch",
        MarketingObjective.Community => "community",
        MarketingObjective.Partnerships => "partnerships",
        _ => "lifestyle",
    };

    private static (string? City, string? Country) ParseLocation(string? primaryLocation)
    {
        if (string.IsNullOrWhiteSpace(primaryLocation))
            return (null, null);

        var parts = primaryLocation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? (parts[0], parts[^1])
            : (parts.Length == 1 ? parts[0] : null, null);
    }
}
