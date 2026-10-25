namespace KultureCompass.Application.Abstractions;

/// <summary>Hands a Queued plan id to the background worker (BE-01 §4). In-process only — a restart loses the queue,
/// which StuckPlanRecovery repairs from the database on startup.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "It is a queue; name fixed by BE-01 §4.")]
public interface IGenerationQueue
{
    ValueTask EnqueueAsync(Guid planId, CancellationToken ct);
}
