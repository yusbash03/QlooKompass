using System.ComponentModel.DataAnnotations;

namespace KultureCompass.Infrastructure.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string[] AdditionalOrigins { get; set; } = [];
}
