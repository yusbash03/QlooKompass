using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    [Range(1, 365)]
    public int RefreshTokenAbsoluteDays { get; set; } = 30;

    public bool CookieSecure { get; set; } = true;

    public const int MinSigningKeyBytes = 32;

    /// <summary>Committed, non-secret values (appsettings.json local-dev key, .env.example placeholder). Production
    /// startup refuses them.</summary>
    public static readonly IReadOnlySet<string> KnownPlaceholderKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "z7MfgP71HDHTQiNagjq5rfDbG/3New6hjqz7RqYMicDkQCtqR7QLGrJynJc5aGOb",
        "replace-with-openssl-rand-base64-48",
    };

    /// <summary>Startup guard (02-security §1): key must be base64 of at least 32 bytes.</summary>
    public bool HasValidSigningKey()
    {
        var buffer = new byte[SigningKey.Length];
        return Convert.TryFromBase64String(SigningKey, buffer, out var written) && written >= MinSigningKeyBytes;
    }
}
