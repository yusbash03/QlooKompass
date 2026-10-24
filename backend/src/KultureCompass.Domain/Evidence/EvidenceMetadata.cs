namespace KultureCompass.Domain.Evidence;

public sealed record EvidenceMetadata(
    string? ShortDescription,
    string? ImageUrl,
    string? ExternalUrl,
    int? ReleaseYear,
    string? Location)
{
    public static readonly EvidenceMetadata Empty = new(null, null, null, null, null);
}
