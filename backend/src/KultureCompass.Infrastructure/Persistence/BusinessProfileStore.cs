using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace KultureCompass.Infrastructure.Persistence;

public sealed class BusinessProfileStore(AppDbContext db) : IBusinessProfileStore
{
    public async Task AddAsync(BusinessProfile profile, CancellationToken ct)
    {
        await db.BusinessProfiles.AddAsync(profile, ct);
    }

    public Task<BusinessProfile?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.BusinessProfiles.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct);

    public async Task<(IReadOnlyList<BusinessProfileSummary> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.BusinessProfiles.AsNoTracking().Where(p => p.OwnerId == ownerId);
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new BusinessProfileSummary(
                p.Id,
                p.BrandName,
                p.Category,
                p.PrimaryLocation,
                p.UpdatedAt,
                db.GrowthPlans.Count(g => g.BusinessProfileId == p.Id && g.OwnerId == ownerId),
                db.GrowthPlans
                    .Where(g => g.BusinessProfileId == p.Id && g.OwnerId == ownerId)
                    .OrderByDescending(g => g.CreatedAt)
                    .Select(g => (GenerationStatus?)g.Status)
                    .FirstOrDefault()))
            .ToListAsync(ct);
        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
