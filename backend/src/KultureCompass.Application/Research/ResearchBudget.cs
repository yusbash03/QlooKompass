namespace KultureCompass.Application.Research;

/// <summary>Hard cap on Qloo calls for one plan (BE-03 §4, <c>Qloo:MaxCallsPerPlan</c>, default 12). Thread-safe:
/// <see cref="CulturalIntelligenceService"/> runs ops at bounded concurrency and must never issue a call once the
/// budget is spent.</summary>
public sealed class ResearchBudget(int maxCalls)
{
    private int _callsMade;

    public int MaxCalls { get; } = maxCalls > 0 ? maxCalls : throw new ArgumentOutOfRangeException(nameof(maxCalls), "MaxCalls must be positive.");

    public int CallsMade => _callsMade;

    public bool HasRemaining => Volatile.Read(ref _callsMade) < MaxCalls;

    /// <summary>Atomically reserves one call slot. Returns false (and reserves nothing) if the budget is already spent.</summary>
    public bool TryReserve()
    {
        while (true)
        {
            var current = Volatile.Read(ref _callsMade);
            if (current >= MaxCalls)
                return false;
            if (Interlocked.CompareExchange(ref _callsMade, current + 1, current) == current)
                return true;
        }
    }
}
