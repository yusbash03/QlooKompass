using KultureCompass.Domain.Evidence;

namespace KultureCompass.Application.Research;

/// <summary>Quality-label counts, combined limitations, and the min-evidence gate (BE-03 §4: "If total usable
/// evidence &lt; Generation:MinEvidenceItems -> plan Failed(qloo_no_evidence)"). Pure and deterministic — the
/// future BE-6 pipeline acts on <see cref="HasSufficientEvidence"/>; this type only states the fact.</summary>
public sealed record EvidenceEvaluation(
    bool HasSufficientEvidence,
    IReadOnlyDictionary<EvidenceQuality, int> CountsByQuality,
    IReadOnlyList<string> Limitations,
    string? InsufficientEvidenceMessage);

public static class EvidenceEvaluator
{
    public const string InsufficientEvidenceMessage =
        "Add 1-3 reference brands or artists your customers love so we can retrieve more cultural evidence.";

    public static EvidenceEvaluation Evaluate(EvidenceSet evidenceSet, int minEvidenceItems)
    {
        ArgumentNullException.ThrowIfNull(evidenceSet);
        if (minEvidenceItems < 1)
            throw new ArgumentOutOfRangeException(nameof(minEvidenceItems), "minEvidenceItems must be at least 1.");

        var counts = Enum.GetValues<EvidenceQuality>()
            .ToDictionary(quality => quality, quality => evidenceSet.Items.Count(item => item.Quality == quality));

        var sufficient = evidenceSet.Items.Count >= minEvidenceItems;

        return new EvidenceEvaluation(
            sufficient,
            counts,
            evidenceSet.Limitations,
            sufficient ? null : InsufficientEvidenceMessage);
    }
}
