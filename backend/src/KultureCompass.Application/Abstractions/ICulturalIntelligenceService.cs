using KultureCompass.Application.Research;

namespace KultureCompass.Application.Abstractions;

/// <summary>Deterministic research orchestrator (BE-03 §4). The LLM never calls Qloo directly or chooses
/// endpoints — only this service does, via <see cref="ResearchPlanner"/>'s fixed op menu. Implementation:
/// <see cref="CulturalIntelligenceService"/>.</summary>
public interface ICulturalIntelligenceService
{
    Task<EvidenceSet> RetrieveAsync(ResearchBrief brief, ResearchBudget budget, IProgress<string>? progress, CancellationToken ct);
}
