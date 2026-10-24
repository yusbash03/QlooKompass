namespace KultureCompass.Domain.Plans;

public sealed record StageLogEntry(
    GenerationStage Stage,
    StageState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Detail);
