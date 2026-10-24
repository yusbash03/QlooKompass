using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

/// <summary>
/// Config-bound generation limits (section "Generation"). Distinct from the per-request
/// <see cref="KultureCompass.Domain.Plans.GenerationOptions"/> domain type — deliberately named
/// differently to avoid a naming collision between the two unrelated concepts.
/// </summary>
public sealed class GenerationSettings
{
    public const string SectionName = "Generation";

    [Range(1, 1000)]
    public int DailyLimitPerUser { get; set; } = 10;

    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 180;

    [Range(1, 100)]
    public int MinEvidenceItems { get; set; } = 5;

    [Range(1, 500)]
    public int MaxEvidenceItems { get; set; } = 60;
}
