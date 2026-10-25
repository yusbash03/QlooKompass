using KultureCompass.Application.Profiles;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Abstractions;

public interface IBusinessProfileStore
{
    Task AddAsync(BusinessProfile profile, CancellationToken ct);

    /// <summary>Tracked; null when missing or owned by someone else (callers map both to 404).</summary>
    Task<BusinessProfile?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    Task<(IReadOnlyList<BusinessProfileSummary> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Saves with the row-version check; false when another writer changed the row first.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct);
}
