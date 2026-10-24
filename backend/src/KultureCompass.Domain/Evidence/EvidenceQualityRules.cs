using KultureCompass.Domain.Common;

namespace KultureCompass.Domain.Evidence;

/// Deterministic evidence-quality classification per docs/backend/BE-03-qloo-integration.md §5.
/// This is a heuristic based on how an item was retrieved from Qloo, never an LLM-made probability.
public static class EvidenceQualityRules
{
    public const double DefaultStrongAffinityThreshold = 0.80;

    public static EvidenceQualityResult Classify(EvidenceQualityInput input, double strongAffinityThreshold = DefaultStrongAffinityThreshold)
    {
        var eligibleForHigherRating = input.LocationVerified && !input.SeedMatchedBelowThreshold;

        if (eligibleForHigherRating && input.IsUserProvidedSeedSignal)
        {
            var reasons = new List<string> { "user-provided seed signal" };
            var strongByAffinity = input.Affinity is { } a && a >= strongAffinityThreshold;
            var strongByRepetition = input.AppearedInOperations >= 2;
            if (strongByAffinity)
                reasons.Add($"affinity {input.Affinity:0.00} >= {strongAffinityThreshold:0.00}");
            if (strongByRepetition)
                reasons.Add($"seen in {input.AppearedInOperations} operations");
            if (strongByAffinity || strongByRepetition)
                return new EvidenceQualityResult(EvidenceQuality.Strong, reasons);
        }

        if (eligibleForHigherRating)
        {
            if (input.Affinity is not null && !input.IsTagOnlySignal)
                return new EvidenceQualityResult(EvidenceQuality.Moderate, [$"affinity {input.Affinity:0.00} returned"]);

            if (input.IsAiSuggestedSeedSignal)
                return new EvidenceQualityResult(EvidenceQuality.Moderate, ["AI-suggested seed"]);

            if (input.IsTagOnlySignal && input.Affinity is { } tagAffinity && tagAffinity >= strongAffinityThreshold)
                return new EvidenceQualityResult(EvidenceQuality.Moderate,
                    [$"tag-only signal with affinity {tagAffinity:0.00} >= {strongAffinityThreshold:0.00}"]);
        }

        var weakReasons = new List<string>();
        if (!input.LocationVerified)
            weakReasons.Add("location unverified");
        if (input.SeedMatchedBelowThreshold)
            weakReasons.Add("seed matched below threshold but kept for context");
        if (input.IsTagOnlySignal && input.Affinity is null)
            weakReasons.Add("tag-only signal without affinity");
        else if (input.IsTagOnlySignal && input.Affinity is not null)
            weakReasons.Add($"tag-only signal with affinity {input.Affinity:0.00} below {strongAffinityThreshold:0.00}");
        if (input.Affinity is null && !input.IsTagOnlySignal)
            weakReasons.Add("no affinity returned");
        if (weakReasons.Count == 0)
            weakReasons.Add("insufficient signal for a higher confidence rating");

        return new EvidenceQualityResult(EvidenceQuality.Weak, weakReasons);
    }

    /// Recommendation.EvidenceStrength = Strong if >= 1 Strong ref and >= 2 refs total; Weak if all refs Weak; else Moderate.
    public static EvidenceQuality AggregateStrength(IEnumerable<EvidenceQuality> referencedQualities)
    {
        var list = referencedQualities.ToList();
        if (list.Count == 0)
            throw new DomainException("Cannot aggregate evidence strength from an empty set.");

        if (list.Count >= 2 && list.Any(q => q == EvidenceQuality.Strong))
            return EvidenceQuality.Strong;

        if (list.All(q => q == EvidenceQuality.Weak))
            return EvidenceQuality.Weak;

        return EvidenceQuality.Moderate;
    }
}
