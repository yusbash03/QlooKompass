namespace KultureCompass.Domain.Evaluations;

public sealed record EvaluationMetrics(
    int NamedEntities,
    int TraceableEntities,
    int UnsupportedEntities,
    double? RecommendationsWithEvidencePct,
    int UnsupportedNumericClaims,
    double AvgEntitiesPerRecommendation,
    double BriefTermCoveragePct);
