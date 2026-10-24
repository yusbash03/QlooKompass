using KultureCompass.Domain.Common;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Experiments;
using KultureCompass.Domain.Recommendations;

namespace KultureCompass.Domain.Plans;

public sealed class GrowthPlan
{
    private readonly List<StageLogEntry> _stageLog = [];
    private readonly List<EvidenceItem> _evidence = [];
    private readonly List<Recommendation> _recommendations = [];
    private readonly List<MarketingExperiment> _experiments = [];
    private readonly List<string> _limitations = [];
    private readonly List<UnlinkedMention> _unlinkedMentions = [];

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid BusinessProfileId { get; private set; }
    public GenerationStatus Status { get; private set; }
    public GenerationStage Stage { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public BriefSnapshot BriefSnapshot { get; private set; } = null!;
    public GenerationOptions Options { get; private set; } = null!;
    public string? InterpretationJson { get; private set; }
    public string? ResearchPlanJson { get; private set; }
    public GrowthPlanSummary? Summary { get; private set; }
    public string SchemaVersion { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public QlooMode QlooMode { get; private set; }
    public LlmProvider LlmProvider { get; private set; }
    public string? LlmModel { get; private set; }
    public bool IsMock { get; private set; }
    public int QlooCallCount { get; private set; }
    public int LlmCallCount { get; private set; }
    public string? LlmUsageJson { get; private set; }
    public PlanError? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<StageLogEntry> StageLog => _stageLog;
    public IReadOnlyList<EvidenceItem> Evidence => _evidence;
    public IReadOnlyList<Recommendation> Recommendations => _recommendations;
    public IReadOnlyList<MarketingExperiment> Experiments => _experiments;
    public IReadOnlyList<string> Limitations => _limitations;
    public IReadOnlyList<UnlinkedMention> UnlinkedMentions => _unlinkedMentions;

    public bool IsTerminal => Status is GenerationStatus.Completed or GenerationStatus.CompletedWithLimitations or GenerationStatus.Failed;

    /// Reserved for EF Core materialization (field-based property access).
    private GrowthPlan()
    {
    }

    private GrowthPlan(
        Guid id, string ownerId, Guid businessProfileId, string idempotencyKey, string requestHash,
        BriefSnapshot briefSnapshot, GenerationOptions options, DateTimeOffset now)
    {
        Id = id;
        OwnerId = ownerId;
        BusinessProfileId = businessProfileId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        BriefSnapshot = briefSnapshot;
        Options = options;
        CreatedAt = now;
        UpdatedAt = now;
        Status = GenerationStatus.Queued;
        Stage = GenerationStage.Queued;
    }

    public static GrowthPlan CreateQueued(
        string ownerId, Guid businessProfileId, string idempotencyKey, string requestHash,
        BriefSnapshot briefSnapshot, GenerationOptions options, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new DomainException("OwnerId is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("IdempotencyKey is required.");
        if (string.IsNullOrWhiteSpace(requestHash))
            throw new DomainException("RequestHash is required.");

        var plan = new GrowthPlan(Guid.CreateVersion7(), ownerId, businessProfileId, idempotencyKey, requestHash, briefSnapshot, options, now);
        plan._stageLog.Add(new StageLogEntry(GenerationStage.Queued, StageState.Active, now, null, null));
        return plan;
    }

    public void Start(RunContext context, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Queued);
        CompleteActiveStageEntry(now);

        SchemaVersion = context.SchemaVersion;
        PromptVersion = context.PromptVersion;
        QlooMode = context.QlooMode;
        LlmProvider = context.LlmProvider;
        LlmModel = context.LlmModel;
        IsMock = context.IsMock;

        Status = GenerationStatus.Running;
        StartedAt = now;
        Stage = GenerationStage.InterpretingBrief;
        _stageLog.Add(new StageLogEntry(GenerationStage.InterpretingBrief, StageState.Active, now, null, null));
        Touch(now);
    }

    public void AdvanceTo(GenerationStage stage, string? detail, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        if (stage <= Stage)
            throw new DomainException($"Cannot advance from stage {Stage} to {stage}; stages must move strictly forward.");

        CompleteActiveStageEntry(now);
        Stage = stage;
        _stageLog.Add(new StageLogEntry(stage, StageState.Active, now, null, detail));
        Touch(now);
    }

    public void RecordDetail(string detail, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        if (_stageLog.Count == 0 || _stageLog[^1].State != StageState.Active)
            throw new DomainException("No active stage to record detail against.");
        _stageLog[^1] = _stageLog[^1] with { Detail = detail };
        Touch(now);
    }

    public void IncrementQlooCallCount(int by = 1)
    {
        EnsureStatus(GenerationStatus.Running);
        QlooCallCount += by;
    }

    public void IncrementLlmCallCount(int by = 1)
    {
        EnsureStatus(GenerationStatus.Running);
        LlmCallCount += by;
    }

    public void SetLlmUsage(string llmUsageJson)
    {
        EnsureStatus(GenerationStatus.Running);
        LlmUsageJson = llmUsageJson;
    }

    public void SetInterpretation(string interpretationJson, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        InterpretationJson = interpretationJson;
        Touch(now);
    }

    public void SetResearchPlan(string researchPlanJson, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        ResearchPlanJson = researchPlanJson;
        Touch(now);
    }

    public void AttachEvidence(IReadOnlyList<EvidenceItem> items, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        _evidence.Clear();
        _evidence.AddRange(items);
        Touch(now);
    }

    public void AddLimitations(IEnumerable<string> limitations, DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        foreach (var limitation in limitations)
        {
            if (!string.IsNullOrWhiteSpace(limitation) && !_limitations.Contains(limitation))
                _limitations.Add(limitation);
        }
        Touch(now);
    }

    public void AttachResults(
        GrowthPlanSummary summary,
        IReadOnlyList<Recommendation> recommendations,
        IReadOnlyList<MarketingExperiment> experiments,
        IReadOnlyList<string> limitations,
        IReadOnlyList<UnlinkedMention> mentions,
        DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);

        var evidenceKeys = _evidence.Select(e => e.Key).ToHashSet();
        foreach (var recommendation in recommendations)
        {
            foreach (var key in recommendation.EvidenceKeys)
            {
                if (!evidenceKeys.Contains(key))
                    throw new DomainException($"Recommendation {recommendation.Id} references unknown evidence key {key}.");
            }

            if (recommendation.Payload is CollaborationPayload collaboration &&
                !evidenceKeys.Contains(collaboration.PartnerEvidenceKey))
            {
                throw new DomainException(
                    $"Collaboration {recommendation.Id} partner evidence key {collaboration.PartnerEvidenceKey} does not exist in this plan's evidence.");
            }
        }

        var recommendationIds = recommendations.Select(r => r.Id).ToHashSet();
        foreach (var experiment in experiments)
        {
            if (!recommendationIds.Contains(experiment.LinkedRecommendationId))
            {
                throw new DomainException(
                    $"Experiment {experiment.Id} links to recommendation {experiment.LinkedRecommendationId}, which does not exist in this plan.");
            }
        }

        Summary = summary;
        _recommendations.Clear();
        _recommendations.AddRange(recommendations);
        _experiments.Clear();
        _experiments.AddRange(experiments);
        _unlinkedMentions.Clear();
        _unlinkedMentions.AddRange(mentions);
        AddLimitations(limitations, now);
        Touch(now);
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        CompleteActiveStageEntry(now);

        var hasLimitations = _limitations.Count > 0;
        var collaborationsRequestedButEmpty = Options.IncludeCollaborations &&
            !_recommendations.Any(r => r.Kind == RecommendationKind.Collaboration);
        var campaignsRequestedButEmpty = Options.IncludeCampaigns &&
            !_recommendations.Any(r => r.Kind == RecommendationKind.Campaign);
        var anyRequestedSectionEmpty = collaborationsRequestedButEmpty || campaignsRequestedButEmpty;

        var strongOrModerateRatio = _evidence.Count == 0
            ? 0d
            : (double)_evidence.Count(e => e.Quality is EvidenceQuality.Strong or EvidenceQuality.Moderate) / _evidence.Count;
        var lowQualityEvidence = strongOrModerateRatio < 0.5;

        Status = hasLimitations || anyRequestedSectionEmpty || lowQualityEvidence
            ? GenerationStatus.CompletedWithLimitations
            : GenerationStatus.Completed;

        Stage = GenerationStage.Done;
        _stageLog.Add(new StageLogEntry(GenerationStage.Done, StageState.Done, now, now, null));
        CompletedAt = now;
        Touch(now);
    }

    public void Fail(PlanError error, DateTimeOffset now)
    {
        if (IsTerminal)
            throw new DomainException($"Cannot fail a plan that is already in a terminal state ({Status}).");

        if (_stageLog.Count > 0 && _stageLog[^1].State == StageState.Active)
            _stageLog[^1] = _stageLog[^1] with { State = StageState.Failed, CompletedAt = now };

        Status = GenerationStatus.Failed;
        Error = error;
        CompletedAt = now;
        Touch(now);
    }

    private void CompleteActiveStageEntry(DateTimeOffset now)
    {
        if (_stageLog.Count == 0)
            return;
        var last = _stageLog[^1];
        if (last.State == StageState.Active)
            _stageLog[^1] = last with { State = StageState.Done, CompletedAt = now };
    }

    private void EnsureStatus(GenerationStatus required)
    {
        if (Status != required)
            throw new DomainException($"Expected plan status {required} but was {Status}.");
    }

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
