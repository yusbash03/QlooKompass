using System.Text;
using KultureCompass.Application.Qloo;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Turns typed requests into the exact verified-form query strings from BE-03 §1. Rejects an insights
/// request with no signal/filter up front — this is what prevents the documented 400
/// "at least one valid signal or filter is required".</summary>
internal static class QlooQueryBuilder
{
    public static bool TryBuildSearchQuery(EntitySearchRequest request, out string path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            path = string.Empty;
            error = "query is required";
            return false;
        }

        var take = Clamp(request.Take, fallback: 3);
        var sb = new StringBuilder("/search?query=").Append(Uri.EscapeDataString(request.Query));

        if (request.Types is { Count: > 0 })
        {
            // BE-0 live probe (2024): /search?types=artist (short form) -> 400; types=urn:entity:artist -> 200.
            sb.Append("&types=").Append(string.Join(',', request.Types.Select(ToUrnTypeName)));
        }

        sb.Append("&take=").Append(take);

        path = sb.ToString();
        error = null;
        return true;
    }

    public static bool TryBuildTagsQuery(string query, int take, out string path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            path = string.Empty;
            error = "query is required";
            return false;
        }

        path = $"/v2/tags?filter.query={Uri.EscapeDataString(query)}&take={Clamp(take, fallback: 10)}";
        error = null;
        return true;
    }

    public static bool TryBuildInsightsQuery(InsightsRequest request, out string path, out string? error)
    {
        var hasSignal = request.SeedEntityIds.Count > 0 || request.SeedTagIds.Count > 0;
        if (!hasSignal)
        {
            path = string.Empty;
            error = "At least one valid signal or filter is required (Qloo returns 400 without one).";
            return false;
        }

        var sb = new StringBuilder("/v2/insights?filter.type=").Append(ToUrnTypeName(request.FilterType));

        if (request.SeedEntityIds.Count > 0)
        {
            sb.Append("&signal.interests.entities=").Append(string.Join(',', request.SeedEntityIds));
        }

        if (request.SeedTagIds.Count > 0)
        {
            sb.Append("&signal.interests.tags=").Append(string.Join(',', request.SeedTagIds));
        }

        if (request.TagTypes.Count > 0)
        {
            sb.Append("&filter.tag.types=").Append(string.Join(',', request.TagTypes));
        }

        if (!string.IsNullOrWhiteSpace(request.LocationQuery))
        {
            sb.Append("&filter.location.query=").Append(Uri.EscapeDataString(request.LocationQuery));
        }

        sb.Append("&take=").Append(Clamp(request.Take, fallback: 10));

        if (request.Explainability)
        {
            sb.Append("&feature.explainability=true");
        }

        path = sb.ToString();
        error = null;
        return true;
    }

    internal static string ToShortTypeName(QlooEntityType type) => type switch
    {
        QlooEntityType.Artist => "artist",
        QlooEntityType.Brand => "brand",
        QlooEntityType.Movie => "movie",
        QlooEntityType.Book => "book",
        QlooEntityType.Podcast => "podcast",
        QlooEntityType.Person => "person",
        QlooEntityType.Place => "place",
        QlooEntityType.Destination => "destination",
        QlooEntityType.TvShow => "tv_show",
        QlooEntityType.VideoGame => "videogame", // confirmed via BE-0 live probe's 400 allowed-values list (no underscore)
        QlooEntityType.Tag => "tag",
        QlooEntityType.Demographics => "demographics",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Qloo entity type."),
    };

    internal static string ToUrnTypeName(QlooEntityType type) => type switch
    {
        QlooEntityType.Tag => "urn:tag",
        QlooEntityType.Demographics => "urn:demographics",
        _ => $"urn:entity:{ToShortTypeName(type)}",
    };

    private static int Clamp(int take, int fallback) => Math.Clamp(take <= 0 ? fallback : take, 1, 25);
}
