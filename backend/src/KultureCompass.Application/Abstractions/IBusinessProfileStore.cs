using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Abstractions;

public interface IBusinessProfileStore
{
    Task AddAsync(BusinessProfile profile, CancellationToken ct);

    Task<BusinessProfile?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    Task<(IReadOnlyList<BusinessProfile> Items, int TotalCount)> ListPagedAsync(
        string ownerId, int page, int pageSize, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
