namespace KultureCompass.Application.Research;

/// <summary>Jaro-Winkler name similarity (BE-03 §4: "normalized-name similarity >= 0.85"). Pure, deterministic,
/// case/whitespace-insensitive. Used by <see cref="SeedMatcher"/> to decide whether a /search hit is confidently
/// the seed the user or AI meant.</summary>
public static class NameSimilarity
{
    public static double JaroWinkler(string? a, string? b)
    {
        var s1 = Normalize(a);
        var s2 = Normalize(b);

        if (s1.Length == 0 && s2.Length == 0)
            return 1.0;
        if (s1.Length == 0 || s2.Length == 0)
            return 0.0;
        if (s1 == s2)
            return 1.0;

        var jaro = JaroSimilarity(s1, s2);
        var commonPrefixLength = CommonPrefixLength(s1, s2, maxLength: 4);
        return jaro + commonPrefixLength * 0.1 * (1 - jaro);
    }

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static double JaroSimilarity(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matchDistance = Math.Max(0, Math.Max(len1, len2) / 2 - 1);

        var s1Matches = new bool[len1];
        var s2Matches = new bool[len2];
        var matches = 0;

        for (var i = 0; i < len1; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, len2);
            for (var j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j])
                    continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
            return 0.0;

        double transpositions = 0;
        var k = 0;
        for (var i = 0; i < len1; i++)
        {
            if (!s1Matches[i])
                continue;
            while (!s2Matches[k])
                k++;
            if (s1[i] != s2[k])
                transpositions++;
            k++;
        }

        transpositions /= 2;

        return ((double)matches / len1 + (double)matches / len2 + (matches - transpositions) / matches) / 3.0;
    }

    private static int CommonPrefixLength(string s1, string s2, int maxLength)
    {
        var n = Math.Min(maxLength, Math.Min(s1.Length, s2.Length));
        var i = 0;
        while (i < n && s1[i] == s2[i])
            i++;
        return i;
    }
}
