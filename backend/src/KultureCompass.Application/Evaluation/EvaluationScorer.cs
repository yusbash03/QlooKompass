using KultureCompass.Application.Generation;
using KultureCompass.Domain.Evaluations;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;
using KultureCompass.Domain.Recommendations;

namespace KultureCompass.Application.Evaluation;

/// <summary>One recommendation's text and the evidence keys it cites (none for LLM-only output).</summary>
public sealed record ScoredRecommendation(string Id, IReadOnlyList<string> Texts, int EvidenceKeyCount);

/// <param name="DeclaredEntityNames">Entities the output itself names: collaboration partners + mentionedEntities
/// (LLM-only) or partners + unlinked mentions (grounded, whose evidence-linked mentions are found by text match).</param>
public sealed record ScoringSubject(IReadOnlyList<ScoredRecommendation> Recommendations, IReadOnlyCollection<string> DeclaredEntityNames);

/// <summary>Deterministic, no LLM-as-judge (BE-04 §4). Entity detection = declared names ∪ retrieved evidence names
/// found verbatim in recommendation text; it is not NER, and the notes say so.</summary>
public static class EvaluationScorer
{
    public static readonly IReadOnlyList<string> Notes =
    [
        "The LLM-only output cannot cite retrieved evidence by construction, so its traceable-entity count is 0.",
        "Entities are counted from names the output declares plus exact matches of retrieved evidence names; this is not full named-entity recognition.",
        "Numeric-claim detection covers percentages, follower/like/view/subscriber counts and engagement rates only.",
        "No winner is declared. Review both outputs side by side.",
    ];

    public static EvaluationMetrics Score(
        ScoringSubject subject, IReadOnlyCollection<string> evidenceNames, string groundingText, IReadOnlyCollection<string> briefTerms)
    {
        var evidence = evidenceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allText = string.Join("\n", subject.Recommendations.SelectMany(r => r.Texts));

        var named = subject.DeclaredEntityNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Concat(evidence.Where(name => allText.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var traceable = named.Count(evidence.Contains);

        var recCount = subject.Recommendations.Count;
        // n/a for LLM-only output (no evidence set to cite), per BE-04 §4.
        double? withEvidencePct = evidence.Count == 0 || recCount == 0
            ? null
            : Percent(subject.Recommendations.Count(r => r.EvidenceKeyCount > 0), recCount);

        var unsupportedNumbers = subject.Recommendations
            .SelectMany(r => r.Texts)
            .SelectMany(OutputValidator.NumericClaims)
            .Count(number => !groundingText.Contains(number, StringComparison.Ordinal));

        var avgEntities = recCount == 0
            ? 0
            : Math.Round(subject.Recommendations.Average(r =>
                (double)named.Count(name => r.Texts.Any(t => t.Contains(name, StringComparison.OrdinalIgnoreCase)))), 2);

        var coverage = briefTerms.Count == 0
            ? 0
            : Percent(briefTerms.Count(term => allText.Contains(term, StringComparison.OrdinalIgnoreCase)), briefTerms.Count);

        return new EvaluationMetrics(named.Count, traceable, named.Count - traceable, withEvidencePct, unsupportedNumbers, avgEntities, coverage);
    }

    /// <summary>Category, objective, city and country words from the brief.</summary>
    public static IReadOnlyList<string> BriefTerms(BriefSnapshot brief)
    {
        var terms = new List<string>
        {
            brief.Category == BusinessCategory.Other && !string.IsNullOrWhiteSpace(brief.CategoryOther) ? brief.CategoryOther! : brief.Category switch
            {
                BusinessCategory.FootwearAccessories => "footwear",
                _ => brief.Category.ToString(),
            },
            brief.MarketingObjective.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(brief.PrimaryLocation))
            terms.AddRange(brief.PrimaryLocation.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return [.. terms.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static ScoringSubject FromPlan(GrowthPlan plan) => new(
        [.. plan.Recommendations.Select(r => new ScoredRecommendation(r.Id, Texts(r), r.EvidenceKeys.Count))],
        [
            .. plan.Recommendations.Select(r => r.Payload).OfType<CollaborationPayload>().Select(c => c.PartnerName),
            .. plan.UnlinkedMentions.Select(m => m.Name),
        ]);

    public static ScoringSubject FromDraft(GrowthPlanDraft draft) => new(
        [
            .. (draft.Insights ?? []).Select(i => new ScoredRecommendation(i.Id ?? "", [i.Title ?? "", i.Finding ?? "", i.WhyItMatters ?? ""], 0)),
            .. (draft.Collaborations ?? []).Select(c => new ScoredRecommendation(c.Id ?? "",
                [c.PartnerName ?? "", c.CulturalConnection ?? "", c.Format ?? "", c.FitRationale ?? "", c.TestIdea ?? ""], 0)),
            .. (draft.Campaigns ?? []).Select(c => new ScoredRecommendation(c.Id ?? "",
                [c.Title ?? "", c.Objective ?? "", c.TargetHypothesis ?? "", c.CulturalInsight ?? "", c.Angle ?? "", c.SuccessMetric ?? "", .. c.Execution ?? []], 0)),
        ],
        [
            .. (draft.MentionedEntities ?? []).Select(m => m.Name ?? ""),
            .. (draft.Collaborations ?? []).Select(c => c.PartnerName ?? ""),
        ]);

    private static IReadOnlyList<string> Texts(Recommendation r) => r.Payload switch
    {
        InsightPayload i => [r.Title, i.Finding, i.WhyItMatters],
        CollaborationPayload c => [c.PartnerName, c.CulturalConnection, c.Format, c.FitRationale, c.TestIdea],
        CampaignPayload c => [r.Title, c.Objective, c.TargetHypothesis, c.CulturalInsight, c.Angle, c.SuccessMetric, .. c.Execution],
        _ => [r.Title],
    };

    private static double Percent(int part, int whole) => Math.Round(100d * part / whole, 1);
}
