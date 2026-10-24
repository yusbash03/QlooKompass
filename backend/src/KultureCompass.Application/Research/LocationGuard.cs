using KultureCompass.Application.Qloo;

namespace KultureCompass.Application.Research;

/// <summary>R7 location guard (BE-03 §4): an insights call with a location filter echoes the locality Qloo actually
/// resolved. If its country doesn't match the brief's declared country (or nothing was echoed at all — including
/// the documented "silently resolves to a wrong place" gotcha), the results are untrustworthy and must be
/// discarded.</summary>
public static class LocationGuard
{
    public static bool IsVerified(IReadOnlyList<QlooLocality> echoedLocalities, string? expectedCountry)
    {
        ArgumentNullException.ThrowIfNull(echoedLocalities);

        if (string.IsNullOrWhiteSpace(expectedCountry))
            return false;

        return echoedLocalities.Any(locality =>
            !string.IsNullOrWhiteSpace(locality.Country) &&
            string.Equals(locality.Country.Trim(), expectedCountry.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string UnverifiedLimitation(string locationQuery) =>
        $"Qloo could not resolve '{locationQuery}' reliably; local venue suggestions omitted.";
}
