using KultureCompass.Application.Qloo;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Research;

public sealed record SeedMatchResult(ResolvedSeed? Resolved, string? Limitation);

/// <summary>R1 seed resolution (BE-03 §4): picks the best /search hit for a candidate name, accepting it only if
/// its normalized-name similarity is >= <see cref="SimilarityThreshold"/> AND its type matches the candidate's
/// <see cref="ReferenceKindHint"/> (when the hint isn't Unknown). Below threshold, or ambiguous (two hits >= the
/// threshold of different types with no hint to prefer), the seed is dropped with a limitation — it is never used
/// as a signal for tier-1 insight calls. Pure: takes the hits already returned by a prior /search call, so it has
/// no dependency on <c>IQlooClient</c> and needs no fake client to unit test.</summary>
public static class SeedMatcher
{
    public const double SimilarityThreshold = 0.85;

    public static SeedMatchResult Match(SeedCandidate candidate, IReadOnlyList<QlooEntity> hits)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(hits);

        if (hits.Count == 0)
            return new SeedMatchResult(null, CouldNotMatchLimitation(candidate.Name));

        var acceptableTypes = AcceptableHitTypes(candidate.KindHint);
        var scored = hits
            .Select(hit => (Hit: hit, Score: NameSimilarity.JaroWinkler(candidate.Name, hit.Name)))
            .Where(x => acceptableTypes is null || acceptableTypes.Contains(NormalizeType(x.Hit.Type)))
            .Where(x => x.Score >= SimilarityThreshold)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count == 0)
            return new SeedMatchResult(null, CouldNotMatchLimitation(candidate.Name));

        if (candidate.KindHint == ReferenceKindHint.Unknown)
        {
            var distinctTypes = scored.Select(x => NormalizeType(x.Hit.Type)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (distinctTypes > 1)
                return new SeedMatchResult(null, CouldNotMatchLimitation(candidate.Name));
        }

        return new SeedMatchResult(new ResolvedSeed(candidate, scored[0].Hit), null);
    }

    private static string CouldNotMatchLimitation(string name) => $"Could not confidently match '{name}' in Qloo.";

    /// <summary>Types to request on the /search call itself. Null means search untyped (fallback).</summary>
    public static IReadOnlyList<QlooEntityType>? SearchTypesFor(ReferenceKindHint hint) => hint switch
    {
        ReferenceKindHint.Brand => [QlooEntityType.Brand],
        ReferenceKindHint.Artist => [QlooEntityType.Artist],
        ReferenceKindHint.Person => [QlooEntityType.Person],
        ReferenceKindHint.Place => [QlooEntityType.Place, QlooEntityType.Destination],
        _ => null, // Media and Unknown: no single Qloo type covers "media" — search untyped.
    };

    /// <summary>BE-03 §1 doesn't confirm whether <c>type</c> comes back short-form ("brand") or URN-form
    /// ("urn:entity:brand") — the synthetic fixtures use URN-form. Strip the prefix so matching works against
    /// either, until a live probe settles it.</summary>
    public static string NormalizeType(string? type) =>
        string.IsNullOrEmpty(type)
            ? string.Empty
            : type.StartsWith("urn:entity:", StringComparison.OrdinalIgnoreCase) ? type["urn:entity:".Length..] : type;

    internal static HashSet<string>? AcceptableHitTypes(ReferenceKindHint hint) => hint switch
    {
        ReferenceKindHint.Brand => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "brand" },
        ReferenceKindHint.Artist => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "artist" },
        ReferenceKindHint.Person => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "person" },
        ReferenceKindHint.Place => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "place", "destination" },
        ReferenceKindHint.Media => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "movie", "book", "podcast", "tv_show", "video_game" },
        _ => null, // Unknown: any type is acceptable.
    };
}
