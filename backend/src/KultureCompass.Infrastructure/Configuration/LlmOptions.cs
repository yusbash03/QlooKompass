using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Mock | Gemini | OpenAiCompatible</summary>
    [Required]
    public string Provider { get; set; } = "Mock";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-flash-lite-latest";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 60;

    [Range(1, 32000)]
    public int MaxOutputTokens { get; set; } = 6000;

    [Range(0, 10)]
    public int MaxRepairAttempts { get; set; } = 2;

    /// <summary>Debug-level prompt logging. Must never be true in Production (startup guard).</summary>
    public bool LogPrompts { get; set; }

    /// <summary>Upper bound on a provider-requested retry delay (BE-04 §2: "cap 20 s").</summary>
    [Range(0, 60)]
    public int MaxRetryDelaySeconds { get; set; } = 20;

    /// <summary>Mock provider only: Valid | InvalidJsonOnce | UnknownEvidenceKey | Truncated | QuotaExceeded.</summary>
    public string MockScenario { get; set; } = "Valid";

    public bool IsMock => string.Equals(Provider, "Mock", StringComparison.OrdinalIgnoreCase);
}
