namespace KultureCompass.Application.Qloo;

public sealed record EntitySearchRequest
{
    public required string Query { get; init; }

    public IReadOnlyList<QlooEntityType>? Types { get; init; }

    public int Take { get; init; } = 3;
}
