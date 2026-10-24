namespace KultureCompass.Application.Qloo;

/// <summary>Typed builder for a /v2/insights call (BE-03 §3) — never a bare dictionary of query params.
/// <see cref="QlooQueryBuilder"/> rejects a request with no seed/filter signal (the documented 400 case).</summary>
public sealed record InsightsRequest
{
    public required QlooEntityType FilterType { get; init; }

    public IReadOnlyList<string> SeedEntityIds { get; init; } = [];

    public IReadOnlyList<string> SeedTagIds { get; init; } = [];

    public IReadOnlyList<string> TagTypes { get; init; } = [];

    public string? LocationQuery { get; init; }

    public int Take { get; init; } = 10;

    public bool Explainability { get; init; }
}
