using System.Text.Json;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo.Dtos;

namespace KultureCompass.Infrastructure.Qloo;

internal static class QlooMapper
{
    public static QlooEntity ToEntity(QlooEntityDto dto) => new(
        Id: dto.EntityId ?? string.Empty,
        Name: dto.Name ?? string.Empty,
        Type: ResolveType(dto),
        // Qloo's "subtype" is the real entity type (folded into Type above), not a finer-grained classification —
        // nothing deeper is exposed, so Subtype is left unset rather than duplicating Type.
        Subtype: null,
        Popularity: dto.Popularity,
        Affinity: dto.Query?.Affinity,
        Tags: dto.Tags?.Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList()
              ?? (IReadOnlyList<string>)[],
        Properties: ToPropertyMap(dto.Properties));

    /// <summary>/search entities carry the real type in "types[0]"; /v2/insights entities carry it in "subtype"
    /// ("type" there is always the generic literal "urn:entity"). Prefer subtype when present.</summary>
    internal static string ResolveType(QlooEntityDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Subtype))
            return dto.Subtype;
        if (dto.Types is { Count: > 0 } types)
            return types[0];
        return dto.Type ?? string.Empty;
    }

    public static QlooTag ToTag(QlooTagDto dto) => new(
        Id: dto.ResolvedId ?? string.Empty,
        Name: dto.Name ?? string.Empty,
        Type: dto.Type);

    public static IReadOnlyList<QlooEntity> ToEntities(IReadOnlyList<QlooEntityDto>? dtos) =>
        dtos?.Select(ToEntity).ToList() ?? (IReadOnlyList<QlooEntity>)[];

    public static IReadOnlyList<QlooTag> ToTags(IReadOnlyList<QlooTagDto>? dtos) =>
        dtos?.Select(ToTag).ToList() ?? (IReadOnlyList<QlooTag>)[];

    public static IReadOnlyList<QlooLocality> ToLocalities(QlooTopLevelQueryDto? query) =>
        query?.Localities?.Filter?.Select(l => new QlooLocality(l.Name, l.Country ?? ParseCountryFromDisambiguation(l.Disambiguation))).ToList()
        ?? (IReadOnlyList<QlooLocality>)[];

    /// <summary>The echoed locality has no "country" field — only "disambiguation" (e.g. "Lagos, Nigeria"). Takes
    /// the last comma-separated segment, same heuristic as <c>BriefInterpretation.DeriveDeterministically</c>.</summary>
    internal static string? ParseCountryFromDisambiguation(string? disambiguation)
    {
        if (string.IsNullOrWhiteSpace(disambiguation))
            return null;

        var parts = disambiguation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : null;
    }

    private static Dictionary<string, string?> ToPropertyMap(Dictionary<string, JsonElement>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>(properties.Count);
        foreach (var (key, value) in properties)
        {
            result[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
        }

        return result;
    }
}
