using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;

namespace KultureCompass.Application.Tests.Research;

/// <summary>Hand-rolled IQlooClient test double (no mocking library in this repo). Each hook defaults to an empty
/// success so a test only needs to configure the calls it cares about.</summary>
internal sealed class FakeQlooClient : IQlooClient
{
    private static readonly QlooResultMeta Meta = new(TimeSpan.Zero, RateLimitRemaining: null);

    private int _searchEntitiesCallCount;
    private int _searchTagsCallCount;
    private int _getInsightsCallCount;

    public Func<EntitySearchRequest, QlooResult<IReadOnlyList<QlooEntity>>>? OnSearchEntities { get; set; }
    public Func<string, int, QlooResult<IReadOnlyList<QlooTag>>>? OnSearchTags { get; set; }
    public Func<InsightsRequest, QlooResult<InsightsResponse>>? OnGetInsights { get; set; }

    public int SearchEntitiesCallCount => _searchEntitiesCallCount;
    public int SearchTagsCallCount => _searchTagsCallCount;
    public int GetInsightsCallCount => _getInsightsCallCount;

    public Task<QlooResult<IReadOnlyList<QlooEntity>>> SearchEntitiesAsync(EntitySearchRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _searchEntitiesCallCount);
        var result = OnSearchEntities?.Invoke(request) ?? QlooResult<IReadOnlyList<QlooEntity>>.Ok([], Meta);
        return Task.FromResult(result);
    }

    public Task<QlooResult<IReadOnlyList<QlooTag>>> SearchTagsAsync(string query, int take, CancellationToken ct)
    {
        Interlocked.Increment(ref _searchTagsCallCount);
        var result = OnSearchTags?.Invoke(query, take) ?? QlooResult<IReadOnlyList<QlooTag>>.Ok([], Meta);
        return Task.FromResult(result);
    }

    public Task<QlooResult<InsightsResponse>> GetInsightsAsync(InsightsRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _getInsightsCallCount);
        var result = OnGetInsights?.Invoke(request) ?? QlooResult<InsightsResponse>.Ok(new InsightsResponse([], []), Meta);
        return Task.FromResult(result);
    }

    public static QlooEntity Entity(
        string id, string name, string type, double? affinity = null, double? popularity = null,
        string? subtype = null, IReadOnlyList<string>? tags = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        new(id, name, type, subtype, popularity, affinity, tags ?? [], properties ?? new Dictionary<string, string?>());

    public static QlooTag Tag(string id, string name, string? type = "urn:tag:keyword:qloo") => new(id, name, type);
}
