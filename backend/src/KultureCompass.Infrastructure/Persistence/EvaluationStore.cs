using KultureCompass.Application.Abstractions;
using KultureCompass.Domain.Evaluations;
using Microsoft.EntityFrameworkCore;

namespace KultureCompass.Infrastructure.Persistence;

public sealed class EvaluationStore(AppDbContext db) : IEvaluationStore
{
    public async Task AddAsync(EvaluationRun run, CancellationToken ct)
    {
        await db.EvaluationRuns.AddAsync(run, ct);
    }

    public Task<EvaluationRun?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct) =>
        db.EvaluationRuns.FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
