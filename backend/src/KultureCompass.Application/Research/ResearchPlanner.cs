using KultureCompass.Application.Qloo;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Research;

/// <summary>Deterministic op menu builder (BE-03 §4). The LLM never chooses endpoints — it only proposes search
/// terms; this planner maps a <see cref="ResearchBrief"/> to the fixed R1–R8 menu, applying only the ops marked
/// verified in docs/backend/qloo-verified-contract.md. Pure: no I/O, no randomness, same input always produces the
/// same plan — safe to unit test without a Qloo client.</summary>
public sealed class ResearchPlanner(
    bool locationInsightsVerified = false,
    bool personInsightsVerified = false,
    bool demographicsVerified = false)
{
    public const int MaxUserSeeds = 5;
    public const int MaxAiSeeds = 3;
    public const int MaxKeywords = 3;

    public ResearchPlan Plan(ResearchBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var tier0 = new List<PlannedOperation>();

        foreach (var reference in brief.Snapshot.ReferenceEntities.Take(MaxUserSeeds))
        {
            var candidate = new SeedCandidate(reference.Name, reference.KindHint, SeedOrigin.UserProvided);
            tier0.Add(new PlannedOperation(ResearchOperationKind.ResolveSeed, $"R1 resolve seed (user): {reference.Name}", SeedCandidate: candidate));
        }

        foreach (var suggested in brief.Interpretation.SuggestedSeedNames.Take(MaxAiSeeds))
        {
            var candidate = new SeedCandidate(suggested.Name, suggested.KindHint, SeedOrigin.AiSuggested);
            tier0.Add(new PlannedOperation(ResearchOperationKind.ResolveSeed, $"R1 resolve seed (AI): {suggested.Name}", SeedCandidate: candidate));
        }

        foreach (var keyword in brief.Interpretation.TagSearchTerms.Take(MaxKeywords))
        {
            tier0.Add(new PlannedOperation(ResearchOperationKind.ResolveTag, $"R2 resolve tag: {keyword}", Keyword: keyword));
        }

        var hasSeedCandidates = tier0.Any(op => op.Kind == ResearchOperationKind.ResolveSeed);
        var hasAnyCandidates = tier0.Count > 0;

        var tier1 = new List<PlannedOperation>();

        if (hasAnyCandidates)
        {
            tier1.Add(new PlannedOperation(ResearchOperationKind.BrandAffinities, "R3 brand affinities", QlooEntityType.Brand));
            tier1.Add(new PlannedOperation(ResearchOperationKind.ArtistAffinities, "R4 artist affinities", QlooEntityType.Artist));

            if (personInsightsVerified)
                tier1.Add(new PlannedOperation(ResearchOperationKind.ArtistAffinities, "R4 person affinities", QlooEntityType.Person));
        }

        if (hasSeedCandidates)
        {
            tier1.Add(new PlannedOperation(ResearchOperationKind.CulturalThemes, "R5 cultural themes", QlooEntityType.Tag));

            var mediaType = SelectMediaType(brief.Interpretation.Objective);
            tier1.Add(new PlannedOperation(ResearchOperationKind.MediaAffinities, $"R6 media affinities ({mediaType})", mediaType));
        }

        if (hasSeedCandidates && locationInsightsVerified && !string.IsNullOrWhiteSpace(brief.Snapshot.PrimaryLocation))
        {
            tier1.Add(new PlannedOperation(ResearchOperationKind.LocalPlaces, $"R7 local places: {brief.Snapshot.PrimaryLocation}", QlooEntityType.Place));
        }

        if (hasSeedCandidates && demographicsVerified)
        {
            tier1.Add(new PlannedOperation(ResearchOperationKind.Demographics, "R8 demographics", QlooEntityType.Demographics));
        }

        return new ResearchPlan(tier0, tier1);
    }

    /// <summary>Broad-reach objectives (Awareness, Launch) favour tv_show; relationship-building objectives
    /// (Engagement, Sales, Community, Partnerships) favour podcast's narrower, more parasocial audience. A
    /// documented design choice — not a Qloo-verified rule.</summary>
    public static QlooEntityType SelectMediaType(MarketingObjective objective) => objective switch
    {
        MarketingObjective.Awareness or MarketingObjective.Launch => QlooEntityType.TvShow,
        _ => QlooEntityType.Podcast,
    };
}
