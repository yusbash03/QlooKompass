using KultureCompass.Domain.Evidence;

namespace KultureCompass.Application.Research;

/// <summary>Result of <see cref="ICulturalIntelligenceService.RetrieveAsync"/> (BE-03 §4): normalized, deduped
/// evidence with deterministic <c>ev_###</c> keys, plus everything needed for observability (BE-03 §6) and the
/// min-evidence gate (<see cref="EvidenceEvaluator"/>).</summary>
public sealed record EvidenceSet(
    IReadOnlyList<EvidenceItem> Items,
    IReadOnlyList<string> Limitations,
    int CallsMade,
    IReadOnlyList<string> OperationsExecuted)
{
    public static readonly EvidenceSet Empty = new([], [], 0, []);
}
