using KultureCompass.Domain.Plans;

namespace KultureCompass.Application.Abstractions;

public interface IGrowthPlanStore
{
    Task AddAsync(GrowthPlan plan, CancellationToken ct);

    /// Owner-scoped detail read (split query, no tracking) for the GET endpoints.
    Task<GrowthPlan?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    Task<GrowthPlan?> GetByIdempotencyKeyAsync(string ownerId, string idempotencyKey, CancellationToken ct);

    Task<bool> HasActivePlanForProfileAsync(Guid businessProfileId, CancellationToken ct);

    Task<int> CountCreatedSinceAsync(string ownerId, DateTimeOffset sinceUtc, CancellationToken ct);

    Task<(IReadOnlyList<GrowthPlan> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, Guid? businessProfileId, GenerationStatus? status, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
