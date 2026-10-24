using System.Text.Json.Serialization;

namespace KultureCompass.Domain.Recommendations;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(InsightPayload), "Insight")]
[JsonDerivedType(typeof(CollaborationPayload), "Collaboration")]
[JsonDerivedType(typeof(CampaignPayload), "Campaign")]
public abstract record RecommendationPayload;

public sealed record InsightPayload(
    string Finding,
    string WhyItMatters,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Limitations) : RecommendationPayload;

public sealed record CollaborationPayload(
    string PartnerEvidenceKey,
    string PartnerName,
    string CulturalConnection,
    string Format,
    string FitRationale,
    string TestIdea,
    IReadOnlyList<string> Assumptions) : RecommendationPayload;

public sealed record CampaignPayload(
    string Objective,
    string TargetHypothesis,
    string CulturalInsight,
    string Angle,
    IReadOnlyList<string> Execution,
    string SuccessMetric,
    EffortCategory EffortCategory,
    IReadOnlyList<string> Assumptions) : RecommendationPayload;
