namespace KultureCompass.Domain.Evidence;

public sealed record EvidenceQualityInput(
    bool IsUserProvidedSeedSignal,
    bool IsAiSuggestedSeedSignal,
    bool IsTagOnlySignal,
    double? Affinity,
    int AppearedInOperations,
    bool LocationVerified,
    bool SeedMatchedBelowThreshold);

public sealed record EvidenceQualityResult(EvidenceQuality Quality, IReadOnlyList<string> Reasons);
