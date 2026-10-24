using KultureCompass.Domain.Common;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Experiments;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Domain.Recommendations;
using Shouldly;

namespace KultureCompass.Domain.Tests.Plans;

public class GrowthPlanTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BriefSnapshot Brief() => new(
        "Eko Thread Co.", null, BusinessCategory.Streetwear, null, "Graphic tees", null, "Lagos, Nigeria",
        MarketingObjective.Sales, null, null, [], null);

    private static GrowthPlan CreateQueued(GenerationOptions? options = null) =>
        GrowthPlan.CreateQueued("owner-1", Guid.CreateVersion7(), "idem-1", "hash-1", Brief(), options ?? GenerationOptions.Default, T0);

    private static RunContext MockRunContext() => new("1.0", "synth-v1", QlooMode.Mock, LlmProvider.Mock, null, true);

    private static EvidenceItem MakeEvidence(string key, EvidenceQuality quality) => EvidenceItem.Create(
        key, $"qloo-{key}", $"Synthetic {key}", "urn:entity:brand", null, SourceOperation.Insights,
        ["insights: seeds=ev_001"], 0.9, 0.5, [], EvidenceMetadata.Empty, quality, ["test reason"], 1, T0);

    [Fact]
    public void CreateQueued_starts_in_queued_status_with_active_stage_log_entry()
    {
        var plan = CreateQueued();
        plan.Status.ShouldBe(GenerationStatus.Queued);
        plan.Stage.ShouldBe(GenerationStage.Queued);
        plan.StageLog.Count.ShouldBe(1);
        plan.StageLog[0].State.ShouldBe(StageState.Active);
    }

    [Fact]
    public void Start_transitions_to_running_and_first_stage()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));

        plan.Status.ShouldBe(GenerationStatus.Running);
        plan.Stage.ShouldBe(GenerationStage.InterpretingBrief);
        plan.StageLog[0].State.ShouldBe(StageState.Done);
        plan.StageLog[1].Stage.ShouldBe(GenerationStage.InterpretingBrief);
        plan.StageLog[1].State.ShouldBe(StageState.Active);
    }

    [Fact]
    public void Start_throws_when_not_queued()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        Should.Throw<DomainException>(() => plan.Start(MockRunContext(), T0.AddSeconds(2)));
    }

    [Fact]
    public void AdvanceTo_moves_stage_forward_and_closes_previous_entry()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AdvanceTo(GenerationStage.PlanningResearch, "planning", T0.AddSeconds(2));

        plan.Stage.ShouldBe(GenerationStage.PlanningResearch);
        plan.StageLog[1].State.ShouldBe(StageState.Done);
        plan.StageLog[2].Stage.ShouldBe(GenerationStage.PlanningResearch);
        plan.StageLog[2].Detail.ShouldBe("planning");
    }

    [Fact]
    public void AdvanceTo_throws_when_stage_does_not_move_forward()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AdvanceTo(GenerationStage.PlanningResearch, null, T0.AddSeconds(2));

        Should.Throw<DomainException>(() => plan.AdvanceTo(GenerationStage.InterpretingBrief, null, T0.AddSeconds(3)));
        Should.Throw<DomainException>(() => plan.AdvanceTo(GenerationStage.PlanningResearch, null, T0.AddSeconds(3)));
    }

    [Fact]
    public void AdvanceTo_throws_when_plan_not_running()
    {
        var plan = CreateQueued();
        Should.Throw<DomainException>(() => plan.AdvanceTo(GenerationStage.InterpretingBrief, null, T0));
    }

    [Fact]
    public void AttachResults_throws_when_recommendation_references_unknown_evidence_key()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence([MakeEvidence("ev_001", EvidenceQuality.Strong)], T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_999"] = EvidenceQuality.Strong };
        var insight = Recommendation.Create(
            "ins_1", RecommendationKind.Insight, 1, "Title", ["ev_999"], qualities,
            new InsightPayload("finding", "why", [], []));

        Should.Throw<DomainException>(() => plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [insight], [], [], [], T0.AddSeconds(3)));
    }

    [Fact]
    public void AttachResults_throws_when_collaboration_partner_key_missing_from_evidence()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence([MakeEvidence("ev_001", EvidenceQuality.Strong)], T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_001"] = EvidenceQuality.Strong };
        var collab = Recommendation.Create(
            "col_1", RecommendationKind.Collaboration, 1, "Title", ["ev_001"], qualities,
            new CollaborationPayload("ev_999", "Partner", "connection", "format", "fit", "test", []));

        Should.Throw<DomainException>(() => plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [collab], [], [], [], T0.AddSeconds(3)));
    }

    [Fact]
    public void AttachResults_throws_when_experiment_links_to_missing_recommendation()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence([MakeEvidence("ev_001", EvidenceQuality.Strong)], T0.AddSeconds(2));

        var experiment = MarketingExperiment.Create(
            "exp_1", "cam_999", "hypothesis", "segment", "campaign", "metric", 14, "threshold", true, "success", "failure");

        Should.Throw<DomainException>(() => plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [], [experiment], [], [], T0.AddSeconds(3)));
    }

    [Fact]
    public void Complete_is_Completed_when_no_limitations_and_strong_evidence_and_all_sections_present()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence(
            [MakeEvidence("ev_001", EvidenceQuality.Strong), MakeEvidence("ev_002", EvidenceQuality.Strong)],
            T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_001"] = EvidenceQuality.Strong, ["ev_002"] = EvidenceQuality.Strong };
        var insight = Recommendation.Create("ins_1", RecommendationKind.Insight, 1, "Title", ["ev_001"], qualities, new InsightPayload("f", "w", [], []));
        var collab = Recommendation.Create("col_1", RecommendationKind.Collaboration, 2, "Title", ["ev_001"], qualities, new CollaborationPayload("ev_001", "Synthetic ev_001", "c", "f", "fr", "t", []));
        var campaign = Recommendation.Create("cam_1", RecommendationKind.Campaign, 3, "Title", ["ev_002"], qualities, new CampaignPayload("o", "th", "ci", "angle", ["step1", "step2"], "metric", EffortCategory.Low, []));

        plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [insight, collab, campaign], [], [], [], T0.AddSeconds(3));

        plan.Complete(T0.AddSeconds(4));

        plan.Status.ShouldBe(GenerationStatus.Completed);
        plan.Stage.ShouldBe(GenerationStage.Done);
        plan.CompletedAt.ShouldBe(T0.AddSeconds(4));
    }

    [Fact]
    public void Complete_is_CompletedWithLimitations_when_limitations_present()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence([MakeEvidence("ev_001", EvidenceQuality.Strong)], T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_001"] = EvidenceQuality.Strong };
        var insight = Recommendation.Create("ins_1", RecommendationKind.Insight, 1, "Title", ["ev_001"], qualities, new InsightPayload("f", "w", [], []));
        var campaign = Recommendation.Create("cam_1", RecommendationKind.Campaign, 2, "Title", ["ev_001"], qualities, new CampaignPayload("o", "th", "ci", "angle", ["step1", "step2"], "metric", EffortCategory.Low, []));

        plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [insight, campaign], [], ["Qloo returned thin coverage for this location."], [], T0.AddSeconds(3));

        plan.Complete(T0.AddSeconds(4));
        plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations);
    }

    [Fact]
    public void Complete_is_CompletedWithLimitations_when_requested_section_empty()
    {
        var plan = CreateQueued(new GenerationOptions(IncludeCollaborations: true, IncludeCampaigns: true, MaxCampaigns: 3));
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence([MakeEvidence("ev_001", EvidenceQuality.Strong)], T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_001"] = EvidenceQuality.Strong };
        var insight = Recommendation.Create("ins_1", RecommendationKind.Insight, 1, "Title", ["ev_001"], qualities, new InsightPayload("f", "w", [], []));
        var campaign = Recommendation.Create("cam_1", RecommendationKind.Campaign, 2, "Title", ["ev_001"], qualities, new CampaignPayload("o", "th", "ci", "angle", ["step1", "step2"], "metric", EffortCategory.Low, []));

        plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [insight, campaign], [], [], [], T0.AddSeconds(3));

        plan.Complete(T0.AddSeconds(4));
        plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations);
    }

    [Fact]
    public void Complete_is_CompletedWithLimitations_when_evidence_mostly_weak()
    {
        var plan = CreateQueued(new GenerationOptions(false, false, 3));
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.AttachEvidence(
            [MakeEvidence("ev_001", EvidenceQuality.Weak), MakeEvidence("ev_002", EvidenceQuality.Weak), MakeEvidence("ev_003", EvidenceQuality.Strong)],
            T0.AddSeconds(2));

        var qualities = new Dictionary<string, EvidenceQuality> { ["ev_001"] = EvidenceQuality.Weak, ["ev_003"] = EvidenceQuality.Strong };
        var insight = Recommendation.Create("ins_1", RecommendationKind.Insight, 1, "Title", ["ev_003"], qualities, new InsightPayload("f", "w", [], []));
        var campaign = Recommendation.Create("cam_1", RecommendationKind.Campaign, 2, "Title", ["ev_003"], qualities, new CampaignPayload("o", "th", "ci", "angle", ["step1", "step2"], "metric", EffortCategory.Low, []));

        plan.AttachResults(
            new GrowthPlanSummary("Headline that is long enough", "Overview that is sufficiently long for the summary field."),
            [insight, campaign], [], [], [], T0.AddSeconds(3));

        plan.Complete(T0.AddSeconds(4));
        plan.Status.ShouldBe(GenerationStatus.CompletedWithLimitations);
    }

    [Fact]
    public void Fail_from_running_sets_failed_status_and_marks_active_stage_failed()
    {
        var plan = CreateQueued();
        plan.Start(MockRunContext(), T0.AddSeconds(1));
        plan.Fail(new PlanError(PlanErrorCode.QlooNoEvidence, "No usable evidence."), T0.AddSeconds(2));

        plan.Status.ShouldBe(GenerationStatus.Failed);
        plan.Error!.Code.ShouldBe(PlanErrorCode.QlooNoEvidence);
        plan.StageLog[^1].State.ShouldBe(StageState.Failed);
    }

    [Fact]
    public void Fail_from_queued_sets_failed_status()
    {
        var plan = CreateQueued();
        plan.Fail(new PlanError(PlanErrorCode.Timeout, "Stuck in queue."), T0.AddSeconds(1));
        plan.Status.ShouldBe(GenerationStatus.Failed);
    }

    [Fact]
    public void Fail_throws_when_already_terminal()
    {
        var plan = CreateQueued();
        plan.Fail(new PlanError(PlanErrorCode.Timeout, "x"), T0.AddSeconds(1));
        Should.Throw<DomainException>(() => plan.Fail(new PlanError(PlanErrorCode.Timeout, "x"), T0.AddSeconds(2)));
    }

    [Fact]
    public void Mutating_methods_throw_once_plan_is_terminal()
    {
        var plan = CreateQueued();
        plan.Fail(new PlanError(PlanErrorCode.Timeout, "x"), T0.AddSeconds(1));

        Should.Throw<DomainException>(() => plan.AdvanceTo(GenerationStage.InterpretingBrief, null, T0.AddSeconds(2)));
        Should.Throw<DomainException>(() => plan.AttachEvidence([], T0.AddSeconds(2)));
        Should.Throw<DomainException>(() => plan.Complete(T0.AddSeconds(2)));
    }
}
