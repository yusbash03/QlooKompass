using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

/// <summary>Fixed-window limits from 02-security §5 (all per minute).</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int AuthPerMinute { get; set; } = 10;

    [Range(1, 100_000)]
    public int AuthRefreshPerMinute { get; set; } = 30;

    /// <summary>Every endpoint except health; partitioned by user id, else IP.</summary>
    [Range(1, 100_000)]
    public int GlobalPerMinute { get; set; } = 120;

    /// <summary>POST /growth-plans and POST /evaluations, per user.</summary>
    [Range(1, 100_000)]
    public int GenerationPerMinute { get; set; } = 3;

    /// <summary>/meta and /sample-plan, per IP.</summary>
    [Range(1, 100_000)]
    public int PublicPerMinute { get; set; } = 30;
}
