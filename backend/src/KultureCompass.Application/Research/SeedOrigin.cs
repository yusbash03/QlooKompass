namespace KultureCompass.Application.Research;

/// <summary>Provenance of a seed resolved in R1 (BE-03 §4). AI-suggested seeds cap evidence quality at Moderate
/// (see <see cref="KultureCompass.Domain.Evidence.EvidenceQualityInput.IsAiSuggestedSeedSignal"/>).</summary>
public enum SeedOrigin
{
    UserProvided,
    AiSuggested,
}
