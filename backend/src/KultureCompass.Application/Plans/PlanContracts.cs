using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Experiments;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;

namespace KultureCompass.Application.Plans;

public sealed record GenerationOptionsInput(bool? IncludeCollaborations, bool? IncludeCampaigns, int? MaxCampaigns)
{
    public GenerationOptions ToDomain() => new(IncludeCollaborations ?? true, IncludeCampaigns ?? true, MaxCampaigns ?? 3);
}

/// <summary>contracts/openapi.yaml StartGrowthPlanRequest.</summary>
public sealed record StartGrowthPlanRequest(Guid? BusinessProfileId, GenerationOptionsInput? Options);

public sealed record GrowthPlanAcceptedDto(Guid Id, GenerationStatus Status, GenerationStage Stage, string StatusUrl, DateTimeOffset CreatedAt)
{
    public static GrowthPlanAcceptedDto From(GrowthPlan plan) =>
        new(plan.Id, plan.Status, plan.Stage, $"/api/v1/growth-plans/{plan.Id}/status", plan.CreatedAt);
}

/// <summary><see cref="IsTerminalReplay"/> → 200 instead of 202 (01-api-contract §3.1).</summary>
public sealed record StartedPlan(GrowthPlanAcceptedDto Plan, bool IsTerminalReplay);

public sealed record StageLogEntryDto(GenerationStage Stage, string State, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Detail);

public sealed record PlanErrorDto(string Code, string Message)
{
    public static PlanErrorDto? From(PlanError? error) => error is null ? null : new(PlanErrorCodes.ToWire(error.Code), error.Message);
}

public sealed record PlanStatusSnapshot(
    Guid Id, GenerationStatus Status, GenerationStage Stage, IReadOnlyList<StageLogEntry> StageLog, DateTimeOffset UpdatedAt, PlanError? Error);

public sealed record GrowthPlanStatusDto(
    Guid Id, GenerationStatus Status, GenerationStage Stage, IReadOnlyList<StageLogEntryDto> Stages, DateTimeOffset UpdatedAt, PlanErrorDto? Error);

public sealed record EvidenceMetadataDto(string? ShortDescription, string? ImageUrl, string? ExternalUrl, int? ReleaseYear, string? Location);

public sealed record EvidenceDto(
    string Key, string QlooEntityId, string Name, string EntityType, string? Subtype, SourceOperation SourceOperation,
    IReadOnlyList<string> SourceSignals, double? Affinity, double? Popularity, IReadOnlyList<string> Tags,
    EvidenceMetadataDto Metadata, EvidenceQuality Quality, IReadOnlyList<string> QualityReasons, DateTimeOffset RetrievedAt);

public sealed record InsightDto(
    string Id, string Title, string Finding, string WhyItMatters, IReadOnlyList<string> EvidenceKeys, EvidenceQuality EvidenceStrength,
    IReadOnlyList<string> Assumptions, IReadOnlyList<string> Limitations);

public sealed record CollaborationDto(
    string Id, string PartnerEvidenceKey, string PartnerName, string CulturalConnection, string Format, string FitRationale,
    string TestIdea, IReadOnlyList<string> EvidenceKeys, EvidenceQuality EvidenceStrength, IReadOnlyList<string> Assumptions);

public sealed record CampaignDto(
    string Id, string Title, string Objective, string TargetHypothesis, string CulturalInsight, string Angle,
    IReadOnlyList<string> Execution, string SuccessMetric, EffortCategory EffortCategory, IReadOnlyList<string> EvidenceKeys,
    EvidenceQuality EvidenceStrength, IReadOnlyList<string> Assumptions);

public sealed record NextDecisionDto(string IfSuccess, string IfFailure);

public sealed record ExperimentDto(
    string Id, string LinkedRecommendationId, string Hypothesis, string Segment, string ProposedCampaign, string PrimaryMetric,
    int MeasurementPeriodDays, string SuccessThreshold, bool ThresholdIsProposedDefault, NextDecisionDto NextDecision,
    ExperimentStatus Status, string? ObservedResult, string? ResultNotes, string Version,
    DateTimeOffset? StartedAt = null, DateTimeOffset? ConcludedAt = null);

/// <summary>contracts/openapi.yaml ExperimentSummary: one experiment in the cross-plan hub (GET /experiments).</summary>
public sealed record ExperimentSummaryDto(Guid GrowthPlanId, string BrandName, ExperimentDto Experiment);

public sealed record ListExperimentsQuery(int Page, int PageSize, ExperimentStatus? Status);

public sealed record UnlinkedMentionDto(string Name, string Note);

public sealed record PlanSummaryTextDto(string Headline, string Overview);

public sealed record PlanStatsDto(int QlooCalls, int LlmCalls, IReadOnlyDictionary<string, int> EvidenceByQuality);

/// <summary>The BusinessProfileRequest shape (openapi briefSnapshot) frozen at generation time.</summary>
public sealed record BriefSnapshotDto(
    string BrandName, string? Description, Domain.Profiles.BusinessCategory Category, string? CategoryOther, string ProductsDescription,
    string? TargetMarket, string? PrimaryLocation, Domain.Profiles.MarketingObjective MarketingObjective, string? ObjectiveNotes,
    string? BrandIdentity, IReadOnlyList<Profiles.ReferenceEntityDto> ReferenceEntities, Domain.Profiles.BudgetRange? BudgetRange);

/// <summary>contracts/openapi.yaml GrowthPlanDto. Retrieved facts (<see cref="Evidence"/>) and generated hypotheses
/// (insights/collaborations/campaigns/experiments) are separate members; content is empty until the plan is terminal.</summary>
public sealed record GrowthPlanDto(
    Guid Id, Guid BusinessProfileId, string BrandName, GenerationStatus Status, GenerationStage Stage, PlanErrorDto? Error,
    bool IsMock, bool IsSample, QlooMode QlooMode, string LlmProvider, string? LlmModel, string SchemaVersion, string PromptVersion,
    BriefSnapshotDto BriefSnapshot, PlanSummaryTextDto? Summary, IReadOnlyList<EvidenceDto> Evidence, IReadOnlyList<InsightDto> Insights,
    IReadOnlyList<CollaborationDto> Collaborations, IReadOnlyList<CampaignDto> Campaigns, IReadOnlyList<ExperimentDto> Experiments,
    IReadOnlyList<UnlinkedMentionDto> UnlinkedMentions, IReadOnlyList<string> Limitations, PlanStatsDto Stats,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record PlanCountsDto(int Evidence, int Insights, int Collaborations, int Campaigns, int Experiments);

/// <summary>contracts/openapi.yaml GrowthPlanSummary (list item, built by a store projection).</summary>
public sealed record GrowthPlanListItem(
    Guid Id, Guid BusinessProfileId, string BrandName, GenerationStatus Status, string? Headline, bool IsMock,
    PlanCountsDto Counts, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record QuotaDto(int DailyLimit, int UsedToday, int RemainingToday, DateTimeOffset ResetsAt);

public sealed record ListPlansQuery(int Page, int PageSize, Guid? BusinessProfileId, GenerationStatus? Status);

internal static class PlanErrorCodes
{
    public static string ToWire(PlanErrorCode code) => code switch
    {
        PlanErrorCode.QlooUnavailable => "qloo_unavailable",
        PlanErrorCode.QlooAuthFailed => "qloo_auth_failed",
        PlanErrorCode.QlooNoEvidence => "qloo_no_evidence",
        PlanErrorCode.LlmUnavailable => "llm_unavailable",
        PlanErrorCode.LlmQuotaExceeded => "llm_quota_exceeded",
        PlanErrorCode.LlmOutputInvalid => "llm_output_invalid",
        PlanErrorCode.Timeout => "timeout",
        _ => "internal_error",
    };
}
