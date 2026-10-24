using KultureCompass.Domain.Common;
using KultureCompass.Domain.Plans;

namespace KultureCompass.Domain.Evaluations;

public sealed class EvaluationRun
{
    private readonly List<string> _notes = [];

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public Guid BusinessProfileId { get; private set; }
    public Guid? GroundedPlanId { get; private set; }
    public GenerationStatus Status { get; private set; }
    public string? LlmOnlyOutputJson { get; private set; }
    public EvaluationMetrics? GroundedMetrics { get; private set; }
    public EvaluationMetrics? LlmOnlyMetrics { get; private set; }
    public IReadOnlyList<string> Notes => _notes;
    public PlanError? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// Reserved for EF Core materialization (field-based property access).
    private EvaluationRun()
    {
    }

    private EvaluationRun(Guid id, string ownerId, Guid businessProfileId, DateTimeOffset now)
    {
        Id = id;
        OwnerId = ownerId;
        BusinessProfileId = businessProfileId;
        CreatedAt = now;
        Status = GenerationStatus.Queued;
    }

    public static EvaluationRun CreateQueued(string ownerId, Guid businessProfileId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new DomainException("OwnerId is required.");
        return new EvaluationRun(Guid.CreateVersion7(), ownerId, businessProfileId, now);
    }

    public void Start()
    {
        EnsureStatus(GenerationStatus.Queued);
        Status = GenerationStatus.Running;
    }

    public void AttachGroundedPlan(Guid groundedPlanId)
    {
        EnsureStatus(GenerationStatus.Running);
        GroundedPlanId = groundedPlanId;
    }

    public void Complete(
        string llmOnlyOutputJson,
        EvaluationMetrics groundedMetrics,
        EvaluationMetrics llmOnlyMetrics,
        IEnumerable<string> notes,
        DateTimeOffset now)
    {
        EnsureStatus(GenerationStatus.Running);
        LlmOnlyOutputJson = llmOnlyOutputJson;
        GroundedMetrics = groundedMetrics;
        LlmOnlyMetrics = llmOnlyMetrics;
        _notes.AddRange(notes);
        Status = GenerationStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(PlanError error, DateTimeOffset now)
    {
        if (Status is GenerationStatus.Completed or GenerationStatus.Failed)
            throw new DomainException($"Cannot fail an evaluation that is already {Status}.");
        Status = GenerationStatus.Failed;
        Error = error;
        CompletedAt = now;
    }

    private void EnsureStatus(GenerationStatus required)
    {
        if (Status != required)
            throw new DomainException($"Expected evaluation status {required} but was {Status}.");
    }
}
