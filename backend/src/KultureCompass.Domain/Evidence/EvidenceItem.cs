using System.Text.RegularExpressions;
using KultureCompass.Domain.Common;

namespace KultureCompass.Domain.Evidence;

public sealed partial class EvidenceItem
{
    public string Key { get; private set; } = null!;
    public string QlooEntityId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string? Subtype { get; private set; }
    public SourceOperation SourceOperation { get; private set; }
    public IReadOnlyList<string> SourceSignals { get; private set; } = [];
    public double? Affinity { get; private set; }
    public double? Popularity { get; private set; }
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public EvidenceMetadata Metadata { get; private set; } = EvidenceMetadata.Empty;
    public EvidenceQuality Quality { get; private set; }
    public IReadOnlyList<string> QualityReasons { get; private set; } = [];
    public int AppearedInOperations { get; private set; }
    public DateTimeOffset RetrievedAt { get; private set; }

    /// Reserved for EF Core materialization (field-based property access).
    private EvidenceItem()
    {
    }

    private EvidenceItem(
        string key, string qlooEntityId, string name, string entityType, string? subtype,
        SourceOperation sourceOperation, IReadOnlyList<string> sourceSignals, double? affinity, double? popularity,
        IReadOnlyList<string> tags, EvidenceMetadata metadata, EvidenceQuality quality,
        IReadOnlyList<string> qualityReasons, int appearedInOperations, DateTimeOffset retrievedAt)
    {
        Key = key;
        QlooEntityId = qlooEntityId;
        Name = name;
        EntityType = entityType;
        Subtype = subtype;
        SourceOperation = sourceOperation;
        SourceSignals = sourceSignals;
        Affinity = affinity;
        Popularity = popularity;
        Tags = tags;
        Metadata = metadata;
        Quality = quality;
        QualityReasons = qualityReasons;
        AppearedInOperations = appearedInOperations;
        RetrievedAt = retrievedAt;
    }

    [GeneratedRegex("^ev_[0-9]{3}$")]
    private static partial Regex KeyPattern();

    public static EvidenceItem Create(
        string key, string qlooEntityId, string name, string entityType, string? subtype,
        SourceOperation sourceOperation, IReadOnlyList<string> sourceSignals, double? affinity, double? popularity,
        IReadOnlyList<string> tags, EvidenceMetadata metadata, EvidenceQuality quality,
        IReadOnlyList<string> qualityReasons, int appearedInOperations, DateTimeOffset retrievedAt)
    {
        if (!KeyPattern().IsMatch(key))
            throw new DomainException($"Evidence key '{key}' must match pattern ev_###.");
        if (string.IsNullOrWhiteSpace(qlooEntityId))
            throw new DomainException("QlooEntityId is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Evidence Name is required.");
        if (affinity is < 0 or > 1)
            throw new DomainException("Affinity must be between 0 and 1.");
        if (popularity is < 0 or > 1)
            throw new DomainException("Popularity must be between 0 and 1.");
        Guard.MaxItems(tags, nameof(Tags), 10);
        if (appearedInOperations < 1)
            throw new DomainException("AppearedInOperations must be at least 1.");

        return new EvidenceItem(
            key, qlooEntityId, name, entityType, subtype, sourceOperation, sourceSignals, affinity, popularity,
            tags, metadata, quality, qualityReasons, appearedInOperations, retrievedAt);
    }
}
