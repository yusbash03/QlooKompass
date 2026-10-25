using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Recommendations;

namespace KultureCompass.Application.Generation;

public sealed record MappedPlanContent(
    GrowthPlanSummary Summary,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<UnlinkedMention> UnlinkedMentions);

/// <summary>Validated draft → domain objects. Assumes <see cref="OutputValidator"/> passed; evidence strength is
/// aggregated deterministically from the referenced items' quality labels, never taken from the model.</summary>
public static class DraftMapper
{
    public static MappedPlanContent Map(GrowthPlanDraft draft, IReadOnlyList<EvidenceItem> evidence, IReadOnlyList<string> unlinkedMentions)
    {
        var quality = evidence.ToDictionary(e => e.Key, e => e.Quality, StringComparer.Ordinal);
        var recommendations = new List<Recommendation>();

        foreach (var (i, n) in (draft.Insights ?? []).Select((x, n) => (x, n + 1)))
        {
            recommendations.Add(Recommendation.Create(i.Id!, RecommendationKind.Insight, n, i.Title!, i.EvidenceKeys!, quality,
                new InsightPayload(i.Finding!, i.WhyItMatters!, i.Assumptions ?? [], i.Limitations ?? [])));
        }

        foreach (var (c, n) in (draft.Collaborations ?? []).Select((x, n) => (x, n + 1)))
        {
            recommendations.Add(Recommendation.Create(c.Id!, RecommendationKind.Collaboration, n, c.PartnerName!, c.EvidenceKeys!, quality,
                new CollaborationPayload(c.PartnerEvidenceKey!, c.PartnerName!, c.CulturalConnection!, c.Format!, c.FitRationale!,
                    c.TestIdea!, c.Assumptions ?? [])));
        }

        foreach (var (c, n) in (draft.Campaigns ?? []).Select((x, n) => (x, n + 1)))
        {
            recommendations.Add(Recommendation.Create(c.Id!, RecommendationKind.Campaign, n, c.Title!, c.EvidenceKeys!, quality,
                new CampaignPayload(c.Objective ?? string.Empty, c.TargetHypothesis ?? string.Empty, c.CulturalInsight ?? string.Empty,
                    c.Angle!, c.Execution ?? [], c.SuccessMetric!, Enum.Parse<EffortCategory>(c.EffortCategory!), c.Assumptions ?? [])));
        }

        return new MappedPlanContent(
            new GrowthPlanSummary(draft.Summary!.Headline!, draft.Summary.Overview!),
            recommendations,
            draft.Limitations ?? [],
            [.. unlinkedMentions.Select(name => new UnlinkedMention(name, UnlinkedMention.DefaultNote))]);
    }
}
