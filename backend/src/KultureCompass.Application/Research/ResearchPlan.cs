using KultureCompass.Application.Qloo;

namespace KultureCompass.Application.Research;

/// <summary>One planned Qloo request. <see cref="SeedCandidate"/> is set only for <see cref="ResearchOperationKind.ResolveSeed"/>,
/// <see cref="Keyword"/> only for <see cref="ResearchOperationKind.ResolveTag"/>; <see cref="TargetEntityType"/> is
/// set for every tier-1 (insights) op and tells <see cref="CulturalIntelligenceService"/> which <c>filter.type</c>
/// to request.</summary>
public sealed record PlannedOperation(
    ResearchOperationKind Kind,
    string Label,
    QlooEntityType? TargetEntityType = null,
    SeedCandidate? SeedCandidate = null,
    string? Keyword = null);

/// <summary>Deterministic output of <see cref="ResearchPlanner"/> (BE-03 §4): the fixed op menu for one brief, with
/// Tier0 (R1/R2 — seed and tag resolution) always executed before Tier1 (R3–R8, which depend on Tier0's results).
/// Pure data — no I/O has happened yet. <see cref="CulturalIntelligenceService"/> executes it against
/// <see cref="ResearchBudget"/>.</summary>
public sealed record ResearchPlan(IReadOnlyList<PlannedOperation> Tier0, IReadOnlyList<PlannedOperation> Tier1)
{
    public int TotalOperations => Tier0.Count + Tier1.Count;
}
