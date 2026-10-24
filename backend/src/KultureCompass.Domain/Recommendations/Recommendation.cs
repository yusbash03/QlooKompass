using KultureCompass.Domain.Common;
using KultureCompass.Domain.Evidence;

namespace KultureCompass.Domain.Recommendations;

public sealed class Recommendation
{
    public string Id { get; private set; } = null!;
    public RecommendationKind Kind { get; private set; }
    public int Ordinal { get; private set; }
    public string Title { get; private set; } = null!;
    public IReadOnlyList<string> EvidenceKeys { get; private set; } = [];
    public EvidenceQuality EvidenceStrength { get; private set; }
    public RecommendationPayload Payload { get; private set; } = null!;

    /// Reserved for EF Core materialization (field-based property access).
    private Recommendation()
    {
    }

    private Recommendation(
        string id, RecommendationKind kind, int ordinal, string title, IReadOnlyList<string> evidenceKeys,
        EvidenceQuality evidenceStrength, RecommendationPayload payload)
    {
        Id = id;
        Kind = kind;
        Ordinal = ordinal;
        Title = title;
        EvidenceKeys = evidenceKeys;
        EvidenceStrength = evidenceStrength;
        Payload = payload;
    }

    public static Recommendation Create(
        string id,
        RecommendationKind kind,
        int ordinal,
        string title,
        IReadOnlyList<string> evidenceKeys,
        IReadOnlyDictionary<string, EvidenceQuality> evidenceQualityByKey,
        RecommendationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new DomainException("Recommendation Id is required.");
        if (evidenceKeys.Count == 0)
            throw new DomainException($"Recommendation {id} must reference at least one evidence key.");

        var payloadMatchesKind = (kind, payload) switch
        {
            (RecommendationKind.Insight, InsightPayload) => true,
            (RecommendationKind.Collaboration, CollaborationPayload) => true,
            (RecommendationKind.Campaign, CampaignPayload) => true,
            _ => false,
        };
        if (!payloadMatchesKind)
            throw new DomainException($"Recommendation {id} payload type does not match Kind {kind}.");

        var strength = EvidenceQualityRules.AggregateStrength(
            evidenceKeys.Select(k => evidenceQualityByKey.TryGetValue(k, out var q) ? q : EvidenceQuality.Weak));

        return new Recommendation(id, kind, ordinal, title, evidenceKeys, strength, payload);
    }
}
