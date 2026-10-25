using System.Text.Json;
using System.Text.Json.Serialization;

namespace KultureCompass.Application.Generation;

/// <summary>Deserialization target for the LLM's synthesis output (contracts/growth-plan-llm-output.schema.json v1.0).
/// Everything is nullable and mutable: the output is untrusted, and <see cref="OutputValidator"/> sanitises in place
/// (trims, strips HTML, overwrites partner names) before deciding what is valid.</summary>
public sealed class GrowthPlanDraft
{
    public string? SchemaVersion { get; set; }
    public SummaryDraft? Summary { get; set; }
    public List<InsightDraft>? Insights { get; set; }
    public List<CollaborationDraft>? Collaborations { get; set; }
    public List<CampaignDraft>? Campaigns { get; set; }
    public List<ExperimentDraft>? Experiments { get; set; }
    public List<MentionedEntityDraft>? MentionedEntities { get; set; }
    public List<string>? Limitations { get; set; }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    internal static readonly JsonSerializerOptions WriteJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

public sealed class SummaryDraft
{
    public string? Headline { get; set; }
    public string? Overview { get; set; }
}

public interface IRecommendationDraft
{
    string? Id { get; }
    List<string>? EvidenceKeys { get; set; }
    List<string>? Assumptions { get; set; }
}

public sealed class InsightDraft : IRecommendationDraft
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Finding { get; set; }
    public string? WhyItMatters { get; set; }
    public List<string>? EvidenceKeys { get; set; }
    public List<string>? Assumptions { get; set; }
    public List<string>? Limitations { get; set; }
}

public sealed class CollaborationDraft : IRecommendationDraft
{
    public string? Id { get; set; }
    public string? PartnerEvidenceKey { get; set; }
    public string? PartnerName { get; set; }
    public string? CulturalConnection { get; set; }
    public string? Format { get; set; }
    public string? FitRationale { get; set; }
    public string? TestIdea { get; set; }
    public List<string>? EvidenceKeys { get; set; }
    public List<string>? Assumptions { get; set; }
}

public sealed class CampaignDraft : IRecommendationDraft
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Objective { get; set; }
    public string? TargetHypothesis { get; set; }
    public string? CulturalInsight { get; set; }
    public string? Angle { get; set; }
    public List<string>? Execution { get; set; }
    public string? SuccessMetric { get; set; }
    public string? EffortCategory { get; set; }
    public List<string>? EvidenceKeys { get; set; }
    public List<string>? Assumptions { get; set; }
}

public sealed class ExperimentDraft
{
    public string? Id { get; set; }
    public string? LinkedRecommendationId { get; set; }
    public string? Hypothesis { get; set; }
    public string? Segment { get; set; }
    public string? ProposedCampaign { get; set; }
    public string? PrimaryMetric { get; set; }
    public int? MeasurementPeriodDays { get; set; }
    public string? SuccessThreshold { get; set; }
    public bool? ThresholdIsProposedDefault { get; set; }
    public string? IfSuccess { get; set; }
    public string? IfFailure { get; set; }
}

public sealed class MentionedEntityDraft
{
    public string? Name { get; set; }
    public string? EvidenceKey { get; set; }
}
