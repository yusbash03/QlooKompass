using KultureCompass.Domain.Evidence;
using Shouldly;

namespace KultureCompass.Domain.Tests.Evidence;

public class EvidenceQualityRulesTests
{
    public static TheoryData<EvidenceQualityInput, EvidenceQuality> ClassifyCases()
    {
        var data = new TheoryData<EvidenceQualityInput, EvidenceQuality>();

        // Strong: user seed + affinity >= threshold
        data.Add(new EvidenceQualityInput(true, false, false, 0.91, 1, true, false), EvidenceQuality.Strong);
        // Strong: user seed + appeared in >= 2 operations, no affinity
        data.Add(new EvidenceQualityInput(true, false, false, null, 2, true, false), EvidenceQuality.Strong);
        // Moderate: user seed but affinity below threshold and only 1 appearance -> falls through to "affinity present" rule
        data.Add(new EvidenceQualityInput(true, false, false, 0.40, 1, true, false), EvidenceQuality.Moderate);
        // Moderate: AI-suggested seed, no affinity
        data.Add(new EvidenceQualityInput(false, true, false, null, 1, true, false), EvidenceQuality.Moderate);
        // Moderate: tag-only signal with affinity >= threshold
        data.Add(new EvidenceQualityInput(false, false, true, 0.85, 1, true, false), EvidenceQuality.Moderate);
        // Moderate: plain affinity present, not tag-only, not a seed
        data.Add(new EvidenceQualityInput(false, false, false, 0.55, 1, true, false), EvidenceQuality.Moderate);
        // Weak: tag-only without affinity
        data.Add(new EvidenceQualityInput(false, false, true, null, 1, true, false), EvidenceQuality.Weak);
        // Weak: tag-only with affinity below threshold
        data.Add(new EvidenceQualityInput(false, false, true, 0.50, 1, true, false), EvidenceQuality.Weak);
        // Weak: no affinity returned, no seed, not tag-only
        data.Add(new EvidenceQualityInput(false, false, false, null, 1, true, false), EvidenceQuality.Weak);
        // Weak: location unverified overrides an otherwise-strong user seed
        data.Add(new EvidenceQualityInput(true, false, false, 0.95, 3, false, false), EvidenceQuality.Weak);
        // Weak: seed matched below threshold but kept for context
        data.Add(new EvidenceQualityInput(true, false, false, 0.95, 3, true, true), EvidenceQuality.Weak);

        return data;
    }

    [Theory]
    [MemberData(nameof(ClassifyCases))]
    public void Classify_returns_expected_quality(EvidenceQualityInput input, EvidenceQuality expected)
    {
        var result = EvidenceQualityRules.Classify(input);
        result.Quality.ShouldBe(expected);
        result.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public void AggregateStrength_is_strong_when_at_least_one_strong_ref_and_two_total()
    {
        EvidenceQualityRules.AggregateStrength([EvidenceQuality.Strong, EvidenceQuality.Weak])
            .ShouldBe(EvidenceQuality.Strong);
    }

    [Fact]
    public void AggregateStrength_is_not_strong_with_a_single_strong_ref()
    {
        EvidenceQualityRules.AggregateStrength([EvidenceQuality.Strong])
            .ShouldBe(EvidenceQuality.Moderate);
    }

    [Fact]
    public void AggregateStrength_is_weak_when_all_refs_weak()
    {
        EvidenceQualityRules.AggregateStrength([EvidenceQuality.Weak, EvidenceQuality.Weak])
            .ShouldBe(EvidenceQuality.Weak);
    }

    [Fact]
    public void AggregateStrength_is_moderate_otherwise()
    {
        EvidenceQualityRules.AggregateStrength([EvidenceQuality.Moderate, EvidenceQuality.Weak])
            .ShouldBe(EvidenceQuality.Moderate);
    }

    [Fact]
    public void AggregateStrength_throws_on_empty_set()
    {
        Should.Throw<KultureCompass.Domain.Common.DomainException>(() =>
            EvidenceQualityRules.AggregateStrength([]));
    }
}
