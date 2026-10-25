using System.Text.Json;
using System.Text.Json.Serialization;

namespace KultureCompass.Infrastructure.Qloo.Dtos;

/// <summary>snake_case wire DTOs. Shapes below are confirmed against a real hackathon key (see
/// docs/backend/qloo-verified-contract.md) — notably /search and /v2/insights have DIFFERENT envelope and
/// type-field shapes, which earlier (pre-live-probe) BE-03 §1 assumptions got wrong. Every property stays
/// nullable so an unexpected/missing field never throws during deserialization; the mapper fills in safe
/// defaults.</summary>
internal static class QlooJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

/// <summary>Tag shape differs by context: /v2/tags top-level results use "id"; tags embedded on an entity
/// (/search, /v2/insights) use "tag_id". Both are declared so either context deserializes correctly.</summary>
internal sealed record QlooTagDto(string? Id, string? TagId, string? Name, string? Type)
{
    public string? ResolvedId => Id ?? TagId;
}

internal sealed record QlooQueryInfoDto(double? Affinity);

/// <summary>Entity shape also differs by context:
/// - /search: no "type"/"subtype" pair at all — the real type is the first entry of the "types" array.
/// - /v2/insights: "type" is always the generic literal "urn:entity"; the real type lives in "subtype"
///   (e.g. "urn:entity:brand"). <see cref="QlooMapper.ResolveType"/> picks the right one regardless of source.
/// - /v2/insights with filter.type=urn:tag: items arrive under results.tags[] keyed by "tag_id" (no "entity_id"),
///   with the tag type in "subtype" (e.g. "urn:tag:keyword:qloo") — live-verified, so they reuse this DTO.</summary>
internal sealed record QlooEntityDto(
    string? EntityId,
    string? TagId,
    string? Name,
    string? Type,
    string? Subtype,
    IReadOnlyList<string>? Types,
    Dictionary<string, JsonElement>? Properties,
    double? Popularity,
    QlooQueryInfoDto? Query,
    IReadOnlyList<QlooTagDto>? Tags);

/// <summary>The echoed locality (BE-03 §4's R7 location guard) has no "country" field — only "disambiguation"
/// (e.g. "Lagos, Nigeria"), which <see cref="QlooMapper.ParseCountryFromDisambiguation"/> parses. "Country" is
/// kept in case a future response shape adds it directly.</summary>
internal sealed record QlooLocalityDto(string? Name, string? Country, string? Disambiguation);

internal sealed record QlooLocalitiesDto(IReadOnlyList<QlooLocalityDto>? Filter);

internal sealed record QlooTopLevelQueryDto(QlooLocalitiesDto? Localities);

/// <summary>/search's "results" is a flat array of entities — unlike /v2/insights, there is no nested ".entities".</summary>
internal sealed record SearchEnvelopeDto(IReadOnlyList<QlooEntityDto>? Results);

internal sealed record TagsResultsDto(IReadOnlyList<QlooTagDto>? Tags);

internal sealed record TagsEnvelopeDto(TagsResultsDto? Results);

/// <summary>Entity filter types return results.entities[]; filter.type=urn:tag returns results.tags[] instead.</summary>
internal sealed record InsightsResultsDto(IReadOnlyList<QlooEntityDto>? Entities, IReadOnlyList<QlooEntityDto>? Tags);

internal sealed record InsightsEnvelopeDto(InsightsResultsDto? Results, QlooTopLevelQueryDto? Query);
