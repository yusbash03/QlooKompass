using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Plans;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KultureCompass.Infrastructure.Persistence;

public sealed class GrowthPlanStore(AppDbContext db) : IGrowthPlanStore
{
    private static readonly GenerationStatus[] ActiveStatuses = [GenerationStatus.Queued, GenerationStatus.Running];

    public async Task<bool> TryAddAsync(GrowthPlan plan, CancellationToken ct)
    {
        db.GrowthPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(plan).State = EntityState.Detached;
            return false;
        }
    }

    public Task<GrowthPlan?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.GrowthPlans
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);

    public Task<PlanStatusSnapshot?> GetStatusByIdAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.GrowthPlans
            .AsNoTracking()
            .Where(p => p.Id == id && p.OwnerId == ownerId)
            .Select(p => new PlanStatusSnapshot(p.Id, p.Status, p.Stage, p.StageLog, p.UpdatedAt, p.Error))
            .FirstOrDefaultAsync(ct);

    public Task<GrowthPlan?> GetForProcessingAsync(Guid id, CancellationToken ct) =>
        db.GrowthPlans.AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<GrowthPlan>> GetUnfinishedAsync(CancellationToken ct) =>
        await db.GrowthPlans
            .AsSplitQuery()
            .Where(p => ActiveStatuses.Contains(p.Status))
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<GrowthPlan?> GetByIdempotencyKeyAsync(string ownerId, string idempotencyKey, CancellationToken ct) =>
        db.GrowthPlans.AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(p => p.OwnerId == ownerId && p.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> HasActivePlanForProfileAsync(Guid businessProfileId, CancellationToken ct) =>
        db.GrowthPlans.AnyAsync(
            p => p.BusinessProfileId == businessProfileId && ActiveStatuses.Contains(p.Status), ct);

    public Task<int> CountCreatedSinceAsync(string ownerId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        db.GrowthPlans.CountAsync(p => p.OwnerId == ownerId && p.CreatedAt >= sinceUtc, ct);

    public async Task<(IReadOnlyList<GrowthPlanListItem> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, Guid? businessProfileId, GenerationStatus? status, CancellationToken ct)
    {
        var query = db.GrowthPlans.AsNoTracking().Where(p => p.OwnerId == ownerId);
        if (businessProfileId is { } profileId)
            query = query.Where(p => p.BusinessProfileId == profileId);
        if (status is { } s)
            query = query.Where(p => p.Status == s);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.BusinessProfileId,
                p.BriefSnapshot,
                p.Status,
                p.Summary,
                p.IsMock,
                Evidence = p.Evidence.Count,
                Insights = p.Recommendations.Count(r => r.Kind == RecommendationKind.Insight),
                Collaborations = p.Recommendations.Count(r => r.Kind == RecommendationKind.Collaboration),
                Campaigns = p.Recommendations.Count(r => r.Kind == RecommendationKind.Campaign),
                Experiments = p.Experiments.Count,
                p.CreatedAt,
                p.CompletedAt,
            })
            .ToListAsync(ct);

        return ([.. rows.Select(r => new GrowthPlanListItem(
            r.Id, r.BusinessProfileId, r.BriefSnapshot.BrandName, r.Status, r.Summary?.Headline, r.IsMock,
            new PlanCountsDto(r.Evidence, r.Insights, r.Collaborations, r.Campaigns, r.Experiments), r.CreatedAt, r.CompletedAt))], totalCount);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
