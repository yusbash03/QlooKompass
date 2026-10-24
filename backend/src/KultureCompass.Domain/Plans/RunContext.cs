namespace KultureCompass.Domain.Plans;

public sealed record RunContext(
    string SchemaVersion,
    string PromptVersion,
    QlooMode QlooMode,
    LlmProvider LlmProvider,
    string? LlmModel,
    bool IsMock);
