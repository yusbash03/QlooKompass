using KultureCompass.Application.Abstractions;
using KultureCompass.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace KultureCompass.Infrastructure.Persistence;

public sealed class GrowthPlanStore(AppDbContext db) : IGrowthPlanStore
{
    private static readonly GenerationStatus[] ActiveStatuses = [GenerationStatus.Queued, GenerationStatus.Running];

    public async Task AddAsync(GrowthPlan plan, CancellationToken ct)
    {
        await db.GrowthPlans.AddAsync(plan, ct);
    }

    public Task<GrowthPlan?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.GrowthPlans
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);

    public Task<GrowthPlan?> GetForProcessingAsync(Guid id, CancellationToken ct) =>
        db.GrowthPlans.AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<GrowthPlan?> GetByIdempotencyKeyAsync(string ownerId, string idempotencyKey, CancellationToken ct) =>
        db.GrowthPlans.FirstOrDefaultAsync(p => p.OwnerId == ownerId && p.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> HasActivePlanForProfileAsync(Guid businessProfileId, CancellationToken ct) =>
        db.GrowthPlans.AnyAsync(
            p => p.BusinessProfileId == businessProfileId && ActiveStatuses.Contains(p.Status), ct);

    public Task<int> CountCreatedSinceAsync(string ownerId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        db.GrowthPlans.CountAsync(p => p.OwnerId == ownerId && p.CreatedAt >= sinceUtc, ct);

    public async Task<(IReadOnlyList<GrowthPlan> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, Guid? businessProfileId, GenerationStatus? status, CancellationToken ct)
    {
        var query = db.GrowthPlans.AsNoTracking().Where(p => p.OwnerId == ownerId);
        if (businessProfileId is { } profileId)
            query = query.Where(p => p.BusinessProfileId == profileId);
        if (status is { } s)
            query = query.Where(p => p.Status == s);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
