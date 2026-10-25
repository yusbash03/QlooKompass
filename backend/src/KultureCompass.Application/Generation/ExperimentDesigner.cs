using KultureCompass.Domain.Experiments;
using KultureCompass.Domain.Plans;

namespace KultureCompass.Application.Generation;

/// <summary>BE-04 §1 step 7, no LLM: keeps the validated draft's experiments and gives every campaign that lacks one a
/// deterministic template. Thresholds are always proposed defaults — we never have, or invent, a baseline.</summary>
public static class ExperimentDesigner
{
    public const int DefaultPeriodDays = 14;
    public const string DefaultThreshold = "≥ 20% uplift vs the previous 14 days";

    public static IReadOnlyList<MarketingExperiment> Design(GrowthPlanDraft draft, BriefSnapshot snapshot)
    {
        var experiments = (draft.Experiments ?? [])
            .Select(e => MarketingExperiment.Create(
                e.Id!, e.LinkedRecommendationId!, e.Hypothesis!, Or(e.Segment, DefaultSegment(snapshot)),
                Or(e.ProposedCampaign, e.Hypothesis!), e.PrimaryMetric!, e.MeasurementPeriodDays!.Value, e.SuccessThreshold!,
                thresholdIsProposedDefault: true,
                Or(e.IfSuccess, "Scale this up and repeat with a larger audience."),
                Or(e.IfFailure, "Revisit the angle or move on to the next campaign.")))
            .ToList();

        var usedIds = experiments.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var campaign in draft.Campaigns ?? [])
        {
            if (experiments.Count >= DraftLimits.MaxExperiments)
                break;
            if (experiments.Any(e => e.LinkedRecommendationId == campaign.Id))
                continue;

            var id = Enumerable.Range(1, 9).Select(n => $"exp_{n}").First(candidate => !usedIds.Contains(candidate));
            usedIds.Add(id);
            var segment = DefaultSegment(snapshot);
            experiments.Add(MarketingExperiment.Create(
                id,
                campaign.Id!,
                Truncate($"If we run {campaign.Angle} for {segment}, {campaign.SuccessMetric} will improve.", 500),
                segment,
                Truncate(campaign.Title ?? campaign.Angle!, 500),
                Truncate(campaign.SuccessMetric!, 200),
                DefaultPeriodDays,
                DefaultThreshold,
                thresholdIsProposedDefault: true,
                "Scale the campaign and repeat it with a larger budget.",
                "Revisit the angle or try the next campaign."));
        }

        return experiments;
    }

    private static string DefaultSegment(BriefSnapshot snapshot) =>
        Truncate(string.IsNullOrWhiteSpace(snapshot.TargetMarket) ? "your core audience" : snapshot.TargetMarket!, 300);

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd();
}
