namespace KultureCompass.Application.Research;

/// <summary>The fixed, deterministic op menu from BE-03 §4. The LLM never chooses endpoints — it only proposes
/// search terms (<see cref="SuggestedSeedName"/>, <see cref="BriefInterpretation.TagSearchTerms"/>); this enum is
/// the complete set of requests <see cref="ResearchPlanner"/> is allowed to plan.</summary>
public enum ResearchOperationKind
{
    /// <summary>R1 — resolve a seed name (user reference or AI suggestion) via /search.</summary>
    ResolveSeed,

    /// <summary>R2 — resolve a category/aesthetic keyword via /v2/tags.</summary>
    ResolveTag,

    /// <summary>R3 — brand affinities from resolved seeds/tags.</summary>
    BrandAffinities,

    /// <summary>R4 — artist (and, if verified, person) affinities from resolved seeds/tags.</summary>
    ArtistAffinities,

    /// <summary>R5 — cultural themes (urn:tag insights) from resolved seeds.</summary>
    CulturalThemes,

    /// <summary>R6 — media affinities (podcast or tv_show, chosen by objective) from resolved seeds.</summary>
    MediaAffinities,

    /// <summary>R7 — local places, gated on PrimaryLocation being set and Qloo:LocationInsightsVerified.</summary>
    LocalPlaces,

    /// <summary>R8 — demographics, gated on Qloo:DemographicsVerified.</summary>
    Demographics,
}
