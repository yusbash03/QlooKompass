using KultureCompass.Application.Plans;
using KultureCompass.Domain.Plans;

namespace KultureCompass.Application.Abstractions;

public interface IGrowthPlanStore
{
    /// <summary>Inserts and saves. False when (owner_id, idempotency_key) already exists — a concurrent duplicate.</summary>
    Task<bool> TryAddAsync(GrowthPlan plan, CancellationToken ct);

    /// <summary>Owner-scoped full read (split query, no tracking) for GET /growth-plans/{id}.</summary>
    Task<GrowthPlan?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Owner-scoped status projection (polled every 2-5 s): plan columns only, never the owned
    /// evidence/recommendation/experiment rows that a full entity load always brings with it.</summary>
    Task<PlanStatusSnapshot?> GetStatusByIdAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Tracked, not owner-scoped: for the generation pipeline/worker only, which runs outside any request
    /// and acts on the plan's own owner_id.</summary>
    Task<GrowthPlan?> GetForProcessingAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked, owner-scoped: for edits to a finished plan (experiment tracking).</summary>
    Task<GrowthPlan?> GetForUpdateAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Saves with row-version checks; false when another writer changed the row first.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);

    /// <summary>Tracked Queued + Running plans, oldest first, for startup recovery.</summary>
    Task<IReadOnlyList<GrowthPlan>> GetUnfinishedAsync(CancellationToken ct);

    Task<GrowthPlan?> GetByIdempotencyKeyAsync(string ownerId, string idempotencyKey, CancellationToken ct);

    Task<bool> HasActivePlanForProfileAsync(Guid businessProfileId, CancellationToken ct);

    Task<int> CountCreatedSinceAsync(string ownerId, DateTimeOffset sinceUtc, CancellationToken ct);

    /// <summary>Projection (no evidence/recommendation rows loaded), newest first.</summary>
    Task<(IReadOnlyList<GrowthPlanListItem> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, Guid? businessProfileId, GenerationStatus? status, CancellationToken ct);

    /// <summary>Every experiment across the owner's plans (newest plan first), optionally filtered by status.</summary>
    Task<(IReadOnlyList<ExperimentSummaryDto> Items, int TotalCount)> ListExperimentsAsync(
        string ownerId, int page, int pageSize, Domain.Experiments.ExperimentStatus? status, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
