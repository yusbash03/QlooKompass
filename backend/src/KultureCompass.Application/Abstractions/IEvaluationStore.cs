using KultureCompass.Domain.Evaluations;

namespace KultureCompass.Application.Abstractions;

public interface IEvaluationStore
{
    Task AddAsync(EvaluationRun run, CancellationToken ct);

    Task<EvaluationRun?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
