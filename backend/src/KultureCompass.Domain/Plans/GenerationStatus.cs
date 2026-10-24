namespace KultureCompass.Domain.Plans;

public enum GenerationStatus
{
    Queued,
    Running,
    Completed,
    CompletedWithLimitations,
    Failed,
}
