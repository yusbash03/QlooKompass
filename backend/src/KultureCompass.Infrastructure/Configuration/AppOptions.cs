using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string[] AdditionalOrigins { get; set; } = [];

    /// <summary>Honour X-Forwarded-For/Proto from any proxy. Only enable when the API is reachable solely through a
    /// trusted reverse proxy (e.g. the compose nginx); otherwise clients could spoof their IP for rate limiting.</summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Outside Development: redirect HTTP→HTTPS (HSTS is always sent on HTTPS responses outside Development).</summary>
    public bool EnforceHttps { get; set; } = true;

    /// <summary>02-security §6: request body cap (64 KB).</summary>
    [Range(1024, 1_048_576)]
    public int MaxRequestBodyBytes { get; set; } = 64 * 1024;
}
