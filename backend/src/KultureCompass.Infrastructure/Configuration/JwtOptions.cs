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
}
