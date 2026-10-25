using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

/// <summary>Per-IP fixed-window limits from 02-security §5. Other policies (global, generation, public) land with
/// the endpoints they protect.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int AuthPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int AuthRefreshPerMinute { get; set; } = 30;
}
