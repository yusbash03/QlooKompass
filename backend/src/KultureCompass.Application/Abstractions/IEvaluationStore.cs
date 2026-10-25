using KultureCompass.Domain.Evaluations;

namespace KultureCompass.Application.Abstractions;

public interface IEvaluationStore
{
    Task AddAsync(EvaluationRun run, CancellationToken ct);

    Task<EvaluationRun?> GetByIdAsync(Guid id, string ownerId, CancellationToken ct);

    /// <summary>Tracked; used by the worker-side completer (not owner-scoped).</summary>
    Task<EvaluationRun?> GetByGroundedPlanIdAsync(Guid groundedPlanId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
