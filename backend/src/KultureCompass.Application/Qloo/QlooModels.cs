namespace KultureCompass.Application.Qloo;

/// <summary>Provider-neutral entity returned by search or insights. Shape mirrors BE-03 §1's documented response
/// (entity_id, name, type, subtype, properties, popularity, query.affinity, tags) after mapping from Qloo's DTOs.</summary>
public sealed record QlooEntity(
    string Id,
    string Name,
    string Type,
    string? Subtype,
    double? Popularity,
    double? Affinity,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string?> Properties);

public sealed record QlooTag(string Id, string Name, string? Type);

/// <summary>Echoed locality info from an insights call with a location filter. Used by the R7 location guard
/// (BE-03 §4) to discard results whose resolved country doesn't match the brief's country.</summary>
public sealed record QlooLocality(string? Name, string? Country);

public sealed record InsightsResponse(IReadOnlyList<QlooEntity> Entities, IReadOnlyList<QlooLocality> Localities);
