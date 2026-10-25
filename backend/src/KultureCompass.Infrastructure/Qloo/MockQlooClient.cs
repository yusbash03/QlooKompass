using System.Reflection;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo.Dtos;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Deterministic Qloo substitute for Qloo:Mode=Mock. Serves the same synthetic fixtures the
/// Infrastructure.Tests project uses (embedded from tests/Fixtures/qloo), selected by request shape — never
/// randomly. Every fixture entity name is prefixed "Synthetic " and fixtures carry "_synthetic": true.</summary>
public sealed class MockQlooClient : IQlooClient
{
    private static readonly QlooResultMeta MockMeta = new(TimeSpan.Zero, RateLimitRemaining: null);

    public Task<QlooResult<IReadOnlyList<QlooEntity>>> SearchEntitiesAsync(EntitySearchRequest request, CancellationToken ct)
    {
        var fixtureName = request.Types is { Count: > 0 } types && types.Contains(QlooEntityType.Brand) && !types.Contains(QlooEntityType.Artist)
            ? "search-brand.json"
            : "search-artist.json";

        var envelope = Load<SearchEnvelopeDto>(fixtureName);
        var fixtures = QlooMapper.ToEntities(envelope.Results);

        // Echo the user's own query as the top hit (stable synthetic id) so seed resolution works for ANY brief in
        // mock mode. This never introduces a name the user didn't type: resolved seeds are only used as signals,
        // and every evidence item still comes from the "Synthetic …" insight fixtures.
        var echo = fixtures[0] with
        {
            Id = $"synthetic-mock-seed-{StableId(request.Query)}",
            Name = request.Query,
            Type = request.Types is { Count: > 0 } requested ? QlooQueryBuilder.ToUrnTypeName(requested[0]) : fixtures[0].Type,
        };
        var entities = new[] { echo }.Concat(fixtures).Take(Math.Max(1, request.Take)).ToList();
        return Task.FromResult(QlooResult<IReadOnlyList<QlooEntity>>.Ok(entities, MockMeta));
    }

    public Task<QlooResult<IReadOnlyList<QlooTag>>> SearchTagsAsync(string query, int take, CancellationToken ct)
    {
        var envelope = Load<TagsEnvelopeDto>("tags-streetwear.json");
        var tags = QlooMapper.ToTags(envelope.Results?.Tags).Take(Math.Max(1, take)).ToList();
        return Task.FromResult(QlooResult<IReadOnlyList<QlooTag>>.Ok(tags, MockMeta));
    }

    public Task<QlooResult<InsightsResponse>> GetInsightsAsync(InsightsRequest request, CancellationToken ct)
    {
        var envelope = Load<InsightsEnvelopeDto>(request.FilterType == QlooEntityType.Tag
            ? "insights-tags-from-artist.json"
            : "insights-brand-from-artist.json");
        var entities = QlooMapper.ToInsightsItems(envelope.Results).Take(Math.Max(1, request.Take)).ToList();
        var localities = QlooMapper.ToLocalities(envelope.Query);
        return Task.FromResult(QlooResult<InsightsResponse>.Ok(new InsightsResponse(entities, localities), MockMeta));
    }

    private static string StableId(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))[..12];

    private static TDto Load<TDto>(string fixtureFileName)
    {
        var logicalName = $"Qloo.Fixtures.{fixtureFileName}";
        using var stream = typeof(MockQlooClient).GetTypeInfo().Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Missing embedded Qloo fixture '{logicalName}'.");

        return System.Text.Json.JsonSerializer.Deserialize<TDto>(stream, QlooJson.Options)
            ?? throw new InvalidOperationException($"Qloo fixture '{logicalName}' deserialized to null.");
    }
}
