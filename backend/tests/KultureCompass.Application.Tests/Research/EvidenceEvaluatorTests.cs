using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using Shouldly;

namespace KultureCompass.Application.Tests.Research;

public class EvidenceEvaluatorTests
{
    private static EvidenceItem Item(int index, EvidenceQuality quality) => EvidenceItem.Create(
        key: $"ev_{index:000}",
        qlooEntityId: $"entity-{index}",
        name: $"Entity {index}",
        entityType: "brand",
        subtype: null,
        sourceOperation: SourceOperation.Insights,
        sourceSignals: ["Burna Boy"],
        affinity: 0.9,
        popularity: 0.5,
        tags: [],
        metadata: EvidenceMetadata.Empty,
        quality: quality,
        qualityReasons: ["affinity 0.90 >= 0.80"],
        appearedInOperations: 1,
        retrievedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Evaluate_is_insufficient_when_item_count_is_below_the_minimum()
    {
        var evidenceSet = new EvidenceSet([Item(1, EvidenceQuality.Strong), Item(2, EvidenceQuality.Strong)], [], 2, ["R1"]);

        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, minEvidenceItems: 5);

        evaluation.HasSufficientEvidence.ShouldBeFalse();
        evaluation.InsufficientEvidenceMessage.ShouldBe(EvidenceEvaluator.InsufficientEvidenceMessage);
    }

    [Fact]
    public void Evaluate_is_sufficient_when_item_count_meets_the_minimum()
    {
        var items = Enumerable.Range(1, 5).Select(i => Item(i, EvidenceQuality.Moderate)).ToList();
        var evidenceSet = new EvidenceSet(items, [], 5, ["R1"]);

        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, minEvidenceItems: 5);

        evaluation.HasSufficientEvidence.ShouldBeTrue();
        evaluation.InsufficientEvidenceMessage.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_counts_items_by_quality_label()
    {
        var evidenceSet = new EvidenceSet(
            [Item(1, EvidenceQuality.Strong), Item(2, EvidenceQuality.Strong), Item(3, EvidenceQuality.Weak)], [], 3, ["R1"]);

        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, minEvidenceItems: 1);

        evaluation.CountsByQuality[EvidenceQuality.Strong].ShouldBe(2);
        evaluation.CountsByQuality[EvidenceQuality.Moderate].ShouldBe(0);
        evaluation.CountsByQuality[EvidenceQuality.Weak].ShouldBe(1);
    }

    [Fact]
    public void Evaluate_carries_the_evidence_sets_limitations_through_unchanged()
    {
        var evidenceSet = new EvidenceSet([Item(1, EvidenceQuality.Strong)], ["Could not confidently match 'X' in Qloo."], 1, ["R1"]);

        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, minEvidenceItems: 1);

        evaluation.Limitations.ShouldBe(evidenceSet.Limitations);
    }
}
