using System.Globalization;
using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;

namespace KultureCompass.Application.Plans;

public static class PlanMapper
{
    /// <summary>Pipeline stages shown to the client, in order (Queued/Done are states, not work).</summary>
    private static readonly GenerationStage[] VisibleStages =
    [
        GenerationStage.InterpretingBrief, GenerationStage.PlanningResearch, GenerationStage.RetrievingEvidence,
        GenerationStage.EvaluatingEvidence, GenerationStage.GeneratingRecommendations, GenerationStage.ValidatingOutput,
        GenerationStage.DesigningExperiments, GenerationStage.Saving,
    ];

    public static GrowthPlanStatusDto ToStatus(PlanStatusSnapshot plan) =>
        new(plan.Id, plan.Status, plan.Stage, Stages(plan.StageLog, plan.Status), plan.UpdatedAt, PlanErrorDto.From(plan.Error));

    /// <summary>Every visible stage appears: logged ones with their real state/detail, the rest Pending — or Skipped
    /// once the plan has failed and they can no longer run.</summary>
    internal static IReadOnlyList<StageLogEntryDto> Stages(IReadOnlyList<StageLogEntry> stageLog, GenerationStatus status)
    {
        var logged = stageLog.GroupBy(s => s.Stage).ToDictionary(g => g.Key, g => g.Last());
        return
        [
            .. VisibleStages.Select(stage => logged.TryGetValue(stage, out var entry)
                ? new StageLogEntryDto(stage, entry.State.ToString(), entry.StartedAt, entry.CompletedAt, entry.Detail)
                : new StageLogEntryDto(stage, status == GenerationStatus.Failed ? "Skipped" : "Pending", null, null, null)),
        ];
    }

    public static GrowthPlanDto ToDto(GrowthPlan plan)
    {
        // 01-api-contract: content is null/empty until the plan reaches a terminal state.
        var showContent = plan.IsTerminal;
        var recommendations = showContent ? plan.Recommendations.OrderBy(r => r.Kind).ThenBy(r => r.Ordinal).ToList() : [];

        return new GrowthPlanDto(
            plan.Id, plan.BusinessProfileId, plan.BriefSnapshot.BrandName, plan.Status, plan.Stage, PlanErrorDto.From(plan.Error),
            plan.IsMock, IsSample: false, plan.QlooMode, plan.LlmProvider.ToString(), plan.LlmModel, plan.SchemaVersion, plan.PromptVersion,
            ToBrief(plan.BriefSnapshot),
            showContent && plan.Summary is { } s ? new PlanSummaryTextDto(s.Headline, s.Overview) : null,
            showContent ? [.. plan.Evidence.OrderBy(e => e.Key, StringComparer.Ordinal).Select(ToEvidence)] : [],
            [.. recommendations.Where(r => r.Payload is InsightPayload).Select(ToInsight)],
            [.. recommendations.Where(r => r.Payload is CollaborationPayload).Select(ToCollaboration)],
            [.. recommendations.Where(r => r.Payload is CampaignPayload).Select(ToCampaign)],
            showContent ? [.. plan.Experiments.OrderBy(e => e.Id, StringComparer.Ordinal).Select(ToExperiment)] : [],
            showContent ? [.. plan.UnlinkedMentions.Select(m => new UnlinkedMentionDto(m.Name, m.Note))] : [],
            showContent ? plan.Limitations : [],
            new PlanStatsDto(plan.QlooCallCount, plan.LlmCallCount, Enum.GetValues<EvidenceQuality>()
                .ToDictionary(q => q.ToString(), q => plan.Evidence.Count(e => e.Quality == q))),
            plan.CreatedAt, plan.CompletedAt);
    }

    public static BriefSnapshotDto ToBrief(BriefSnapshot b) => new(
        b.BrandName, b.Description, b.Category, b.CategoryOther, b.ProductsDescription, b.TargetMarket, b.PrimaryLocation,
        b.MarketingObjective, b.ObjectiveNotes, b.BrandIdentity,
        [.. b.ReferenceEntities.Select(r => new ReferenceEntityDto(r.Name, r.KindHint))], b.BudgetRange);

    private static EvidenceDto ToEvidence(EvidenceItem e) => new(
        e.Key, e.QlooEntityId, e.Name, e.EntityType, e.Subtype, e.SourceOperation, e.SourceSignals, e.Affinity, e.Popularity, e.Tags,
        new EvidenceMetadataDto(e.Metadata.ShortDescription, e.Metadata.ImageUrl, e.Metadata.ExternalUrl, e.Metadata.ReleaseYear, e.Metadata.Location),
        e.Quality, e.QualityReasons, e.RetrievedAt);

    private static InsightDto ToInsight(Recommendation r)
    {
        var p = (InsightPayload)r.Payload;
        return new(r.Id, r.Title, p.Finding, p.WhyItMatters, r.EvidenceKeys, r.EvidenceStrength, p.Assumptions, p.Limitations);
    }

    private static CollaborationDto ToCollaboration(Recommendation r)
    {
        var p = (CollaborationPayload)r.Payload;
        return new(r.Id, p.PartnerEvidenceKey, p.PartnerName, p.CulturalConnection, p.Format, p.FitRationale, p.TestIdea,
            r.EvidenceKeys, r.EvidenceStrength, p.Assumptions);
    }

    private static CampaignDto ToCampaign(Recommendation r)
    {
        var p = (CampaignPayload)r.Payload;
        return new(r.Id, r.Title, p.Objective, p.TargetHypothesis, p.CulturalInsight, p.Angle, p.Execution, p.SuccessMetric,
            p.EffortCategory, r.EvidenceKeys, r.EvidenceStrength, p.Assumptions);
    }

    public static ExperimentDto ToExperiment(Domain.Experiments.MarketingExperiment e) => new(
        e.Id, e.LinkedRecommendationId, e.Hypothesis, e.Segment, e.ProposedCampaign, e.PrimaryMetric, e.MeasurementPeriodDays,
        e.SuccessThreshold, e.ThresholdIsProposedDefault, new NextDecisionDto(e.IfSuccess, e.IfFailure), e.Status, e.ObservedResult,
        e.ResultNotes, e.Version.ToString(CultureInfo.InvariantCulture), e.StartedAt, e.ConcludedAt);
}
