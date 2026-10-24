using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Qloo;

public enum QlooMode
{
    Live,
    Mock,
}

/// <summary>Bound from the `Qloo:*` configuration section (see .env.example QLOO_* keys).</summary>
public sealed class QlooOptions
{
    public const string SectionName = "Qloo";

    [Required]
    public string BaseUrl { get; set; } = "https://hackathon.api.qloo.com";

    public string? ApiKey { get; set; }

    public QlooMode Mode { get; set; } = QlooMode.Mock;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 20;

    [Range(1, 20)]
    public int MaxRequestsPerSecond { get; set; } = 4;

    [Range(1, 50)]
    public int MaxCallsPerPlan { get; set; } = 12;

    [Range(0, 1440)]
    public int CacheTtlMinutes { get; set; }

    /// <summary>Gates R7 (local places via <c>filter.location.query</c>) — see docs/backend/qloo-verified-contract.md.
    /// Confirmed working against a real key (BE-0 live probe): 200, entities returned, locality correctly echoed.</summary>
    public bool LocationInsightsVerified { get; set; } = true;

    /// <summary>Gates R4's extra <c>urn:entity:person</c> filter. Confirmed working against a real key.</summary>
    public bool PersonInsightsVerified { get; set; } = true;

    /// <summary>Gates R8 (demographics). Still false: the live probe confirmed the endpoint responds, but its shape
    /// is <c>results.demographics[].query.{age,gender}</c> score buckets, not the entity list every other insights
    /// call returns — <see cref="KultureCompass.Application.Research.CulturalIntelligenceService"/> has no path to
    /// turn that into <c>EvidenceItem</c>s yet. Needs dedicated handling, not just a flag flip.</summary>
    public bool DemographicsVerified { get; set; }
}
