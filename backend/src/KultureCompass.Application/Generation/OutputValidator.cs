using System.Text.Json;
using System.Text.RegularExpressions;
using KultureCompass.Domain.Evidence;

namespace KultureCompass.Application.Generation;

/// <summary>Allowed section sizes for one plan (BE-04 §1 step 6 rule 6, narrowed by the request's GenerationOptions).</summary>
public sealed record DraftLimits(int MaxCollaborations = 4, int MaxCampaigns = 3)
{
    public const int MinInsights = 3;
    public const int MaxInsights = 6;
    public const int MinCampaigns = 1;
    public const int MaxExperiments = 6;
}

/// <summary><paramref name="ItemId"/> is the offending recommendation/experiment id (droppable), or null for a
/// document-level problem. <paramref name="Trimmable"/> marks a document-level "too many items" error that the drop
/// step can fix by trimming; every other document-level error needs a repair.</summary>
public sealed record Violation(string? ItemId, string Message, bool Trimmable = false);

public sealed record OutputValidationResult(
    GrowthPlanDraft? Draft,
    IReadOnlyList<Violation> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> UnlinkedMentions)
{
    public bool IsValid => Draft is not null && Errors.Count == 0;
}

/// <summary>The real guard against hallucination and prompt injection (BE-04 §1 step 6, §5). Pure and deterministic:
/// sanitises the draft in place, then reports errors (which trigger repair/drop) and warnings (which don't).</summary>
public static partial class OutputValidator
{
    public const string IllustrativeFiguresAssumption = "Figures are illustrative, not measured.";

    private static readonly HashSet<string> PartnerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "brand", "artist", "person", "place", "podcast", "tv_show",
    };

    private static readonly HashSet<string> EffortValues = new(StringComparer.Ordinal) { "Low", "Medium", "High", "Unknown" };

    public static OutputValidationResult Validate(
        string rawJson, IReadOnlyList<EvidenceItem> evidence, DraftLimits limits, string groundingText)
    {
        GrowthPlanDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<GrowthPlanDraft>(StripCodeFence(rawJson), GrowthPlanDraft.Json);
        }
        catch (JsonException ex)
        {
            return new(null, [new(null, $"Output is not valid JSON ({ex.Message}). Return a single JSON object, no markdown.")], [], []);
        }

        return draft is null
            ? new(null, [new(null, "Output was JSON null; return the full object.")], [], [])
            : Validate(draft, evidence, limits, groundingText);
    }

    public static OutputValidationResult Validate(
        GrowthPlanDraft draft, IReadOnlyList<EvidenceItem> evidence, DraftLimits limits, string groundingText)
    {
        var errors = new List<Violation>();
        var warnings = new List<string>();
        var byKey = evidence.ToDictionary(e => e.Key, StringComparer.Ordinal);

        draft.Insights ??= [];
        draft.Collaborations ??= [];
        draft.Campaigns ??= [];
        draft.Experiments ??= [];
        draft.MentionedEntities ??= [];
        draft.Limitations ??= [];

        Sanitize(draft);

        if (draft.SchemaVersion != PromptLibrary.SchemaVersion)
            errors.Add(new(null, $"schemaVersion must be \"{PromptLibrary.SchemaVersion}\"."));

        if (draft.Summary is null || Len(draft.Summary.Headline) < 10 || Len(draft.Summary.Overview) < 40)
            errors.Add(new(null, "summary.headline (10-140 chars) and summary.overview (40-900 chars) are required."));

        CheckCount(errors, "insights", draft.Insights.Count, DraftLimits.MinInsights, DraftLimits.MaxInsights);
        CheckCount(errors, "collaborations", draft.Collaborations.Count, 0, limits.MaxCollaborations);
        CheckCount(errors, "campaigns", draft.Campaigns.Count, DraftLimits.MinCampaigns, limits.MaxCampaigns);
        if (draft.Experiments.Count > DraftLimits.MaxExperiments)
            errors.Add(new(null, $"experiments has {draft.Experiments.Count} items; at most {DraftLimits.MaxExperiments} allowed.", Trimmable: true));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var insight in draft.Insights)
        {
            var id = CheckId(errors, ids, insight.Id, "ins", "insight");
            RequireText(errors, id, "title", insight.Title);
            RequireText(errors, id, "finding", insight.Finding);
            RequireText(errors, id, "whyItMatters", insight.WhyItMatters);
            CheckEvidenceKeys(errors, id, insight, byKey);
        }

        var unlinkedNames = draft.MentionedEntities
            .Where(m => !string.IsNullOrWhiteSpace(m.Name) && (m.EvidenceKey is null || !byKey.ContainsKey(m.EvidenceKey)))
            .Select(m => m.Name!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var collaboration in draft.Collaborations)
        {
            var id = CheckId(errors, ids, collaboration.Id, "col", "collaboration");
            CheckEvidenceKeys(errors, id, collaboration, byKey);
            RequireText(errors, id, "culturalConnection", collaboration.CulturalConnection);
            RequireText(errors, id, "format", collaboration.Format);
            RequireText(errors, id, "fitRationale", collaboration.FitRationale);
            RequireText(errors, id, "testIdea", collaboration.TestIdea);

            if (collaboration.PartnerEvidenceKey is null || !byKey.TryGetValue(collaboration.PartnerEvidenceKey, out var partner))
            {
                errors.Add(new(id, $"{id}.partnerEvidenceKey {collaboration.PartnerEvidenceKey ?? "(missing)"} does not exist; partners must be evidence items."));
                continue;
            }

            if (!PartnerTypes.Contains(ShortType(partner.EntityType)))
            {
                errors.Add(new(id, $"{id}.partnerEvidenceKey {partner.Key} is a {ShortType(partner.EntityType)}; partners must be a brand, artist, person, place, podcast or tv_show."));
                continue;
            }

            // Rule 7: the model named a partner it also declared as not-from-evidence → it is not grounded.
            var claimed = collaboration.PartnerName?.Trim();
            if (!string.IsNullOrEmpty(claimed) &&
                !string.Equals(claimed, partner.Name, StringComparison.OrdinalIgnoreCase) &&
                unlinkedNames.Contains(claimed))
            {
                errors.Add(new(id, $"{id} names partner \"{claimed}\", which is not in <evidence>. Use an evidence item as the partner."));
                continue;
            }

            collaboration.PartnerName = partner.Name; // Rule 3: server-side overwrite with the retrieved name.
            collaboration.EvidenceKeys ??= [];
            if (!collaboration.EvidenceKeys.Contains(partner.Key))
                collaboration.EvidenceKeys.Insert(0, partner.Key);
        }

        foreach (var campaign in draft.Campaigns)
        {
            var id = CheckId(errors, ids, campaign.Id, "cam", "campaign");
            RequireText(errors, id, "title", campaign.Title);
            RequireText(errors, id, "angle", campaign.Angle);
            RequireText(errors, id, "successMetric", campaign.SuccessMetric);
            CheckEvidenceKeys(errors, id, campaign, byKey);
            if (campaign.Execution is not { Count: >= 2 })
                errors.Add(new(id, $"{id}.execution needs 2-6 steps."));
            if (campaign.EffortCategory is null || !EffortValues.Contains(campaign.EffortCategory))
            {
                warnings.Add($"{id}.effortCategory \"{campaign.EffortCategory}\" is not Low/Medium/High/Unknown; set to Unknown.");
                campaign.EffortCategory = "Unknown";
            }
        }

        var experimentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var experiment in draft.Experiments)
        {
            var id = CheckId(errors, experimentIds, experiment.Id, "exp", "experiment");
            if (experiment.LinkedRecommendationId is null || !ids.Contains(experiment.LinkedRecommendationId))
                errors.Add(new(id, $"{id}.linkedRecommendationId {experiment.LinkedRecommendationId ?? "(missing)"} is not an insight/collaboration/campaign id in this output."));
            if (experiment.MeasurementPeriodDays is not (>= 1 and <= 90))
                errors.Add(new(id, $"{id}.measurementPeriodDays must be 1-90."));
            RequireText(errors, id, "hypothesis", experiment.Hypothesis);
            RequireText(errors, id, "primaryMetric", experiment.PrimaryMetric);
            RequireText(errors, id, "successThreshold", experiment.SuccessThreshold);
            // BE-04 rule 6: we never have a measured baseline, so any threshold is a proposed default.
            experiment.ThresholdIsProposedDefault = true;
        }

        foreach (var campaign in draft.Campaigns.Where(c => c.Id is not null))
        {
            if (!draft.Experiments.Any(e => e.LinkedRecommendationId == campaign.Id))
                warnings.Add($"{campaign.Id} has no experiment; a default experiment will be designed.");
        }

        foreach (var mention in draft.MentionedEntities.Where(m => m.EvidenceKey is not null && !byKey.ContainsKey(m.EvidenceKey)))
            warnings.Add($"mentionedEntities \"{mention.Name}\" cites unknown key {mention.EvidenceKey}; treated as not retrieved.");

        FlagUngroundedNumbers(draft, groundingText, warnings);

        // Names that are actually in evidence never show as "AI suggestion", even if the model forgot the key.
        var evidenceNames = evidence.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unlinked = unlinkedNames.Where(n => !evidenceNames.Contains(n)).Order(StringComparer.OrdinalIgnoreCase).ToList();

        return new(draft, errors, warnings, unlinked);
    }

    /// <summary>After repairs are exhausted (BE-04 step 6): remove items with item-level errors (plus experiments that
    /// pointed at them) and trim over-long sections. The caller re-validates the result; document-level errors are
    /// never droppable.</summary>
    public static GrowthPlanDraft DropInvalidItems(GrowthPlanDraft draft, IReadOnlyList<Violation> errors, DraftLimits limits, out int droppedCount)
    {
        var bad = errors.Where(e => e.ItemId is not null).Select(e => e.ItemId!).ToHashSet(StringComparer.Ordinal);
        var before = Count(draft);

        draft.Insights = [.. (draft.Insights ?? []).Where(i => !bad.Contains(i.Id ?? "ins_?")).Take(DraftLimits.MaxInsights)];
        draft.Collaborations = [.. (draft.Collaborations ?? []).Where(c => !bad.Contains(c.Id ?? "col_?")).Take(limits.MaxCollaborations)];
        draft.Campaigns = [.. (draft.Campaigns ?? []).Where(c => !bad.Contains(c.Id ?? "cam_?")).Take(limits.MaxCampaigns)];

        var remaining = draft.Insights.Select(i => i.Id).Concat(draft.Collaborations.Select(c => c.Id)).Concat(draft.Campaigns.Select(c => c.Id))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        draft.Experiments = [.. (draft.Experiments ?? [])
            .Where(e => !bad.Contains(e.Id ?? "exp_?") && e.LinkedRecommendationId is not null && remaining.Contains(e.LinkedRecommendationId))
            .Take(DraftLimits.MaxExperiments)];

        droppedCount = before - Count(draft);
        return draft;

        static int Count(GrowthPlanDraft d) =>
            (d.Insights?.Count ?? 0) + (d.Collaborations?.Count ?? 0) + (d.Campaigns?.Count ?? 0) + (d.Experiments?.Count ?? 0);
    }

    /// <summary>"urn:entity:brand" → "brand"; "urn:tag:keyword:qloo" → "tag".</summary>
    public static string ShortType(string entityType)
    {
        if (entityType.StartsWith("urn:tag", StringComparison.Ordinal))
            return "tag";
        var lastColon = entityType.LastIndexOf(':');
        return lastColon >= 0 ? entityType[(lastColon + 1)..] : entityType;
    }

    private static void CheckCount(List<Violation> errors, string section, int count, int min, int max)
    {
        if (count < min || count > max)
            errors.Add(new(null, $"{section} has {count} items; {min}-{max} required.", Trimmable: count > max));
    }

    private static string CheckId(List<Violation> errors, HashSet<string> seen, string? id, string prefix, string kind)
    {
        if (id is null || !Regex.IsMatch(id, $"^{prefix}_[1-9]$"))
        {
            var placeholder = id ?? $"{prefix}_?";
            errors.Add(new(placeholder, $"{kind} id \"{id}\" must match {prefix}_1..{prefix}_9."));
            return placeholder;
        }

        if (!seen.Add(id))
            errors.Add(new(id, $"Duplicate id {id}."));
        return id;
    }

    private static void RequireText(List<Violation> errors, string id, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new(id, $"{id}.{field} is required."));
    }

    private static void CheckEvidenceKeys(
        List<Violation> errors, string id, IRecommendationDraft item, Dictionary<string, EvidenceItem> byKey)
    {
        item.EvidenceKeys = [.. (item.EvidenceKeys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(8)];
        if (item.EvidenceKeys.Count == 0)
            errors.Add(new(id, $"{id}.evidenceKeys must reference at least one evidence key."));

        foreach (var key in item.EvidenceKeys.Where(k => !byKey.ContainsKey(k)))
            errors.Add(new(id, $"{id}.evidenceKeys: {key} does not exist; use only keys from <evidence>."));
    }

    private static void FlagUngroundedNumbers(GrowthPlanDraft draft, string groundingText, List<string> warnings)
    {
        IEnumerable<(IRecommendationDraft Item, IEnumerable<string?> Texts)> items =
        [
            .. draft.Insights!.Select(i => ((IRecommendationDraft)i, (IEnumerable<string?>)[i.Title, i.Finding, i.WhyItMatters])),
            .. draft.Collaborations!.Select(c => ((IRecommendationDraft)c, (IEnumerable<string?>)[c.CulturalConnection, c.Format, c.FitRationale, c.TestIdea])),
            .. draft.Campaigns!.Select(c => ((IRecommendationDraft)c, (IEnumerable<string?>)[c.Title, c.Objective, c.TargetHypothesis, c.CulturalInsight, c.Angle, c.SuccessMetric, .. c.Execution ?? []])),
        ];

        foreach (var (item, texts) in items)
        {
            var claims = texts.Where(t => t is not null)
                .SelectMany(t => NumericClaim().Matches(t!).Select(m => m.Groups["num"].Value))
                .Where(number => !groundingText.Contains(number, StringComparison.Ordinal))
                .Distinct()
                .ToList();
            if (claims.Count == 0)
                continue;

            warnings.Add($"{item.Id} contains figures not present in the brief or evidence ({string.Join(", ", claims)}).");
            item.Assumptions ??= [];
            if (!item.Assumptions.Contains(IllustrativeFiguresAssumption) && item.Assumptions.Count < 4)
                item.Assumptions.Add(IllustrativeFiguresAssumption);
        }
    }

    // ── Sanitisation (rule 8): strip HTML, collapse whitespace, cap to schema maxLength ────────────────────────────

    private static void Sanitize(GrowthPlanDraft d)
    {
        if (d.Summary is not null)
        {
            d.Summary.Headline = Clean(d.Summary.Headline, 140);
            d.Summary.Overview = Clean(d.Summary.Overview, 900);
        }

        foreach (var i in d.Insights!)
        {
            i.Title = Clean(i.Title, 120);
            i.Finding = Clean(i.Finding, 500);
            i.WhyItMatters = Clean(i.WhyItMatters, 500);
            i.Assumptions = CleanList(i.Assumptions, 4, 200);
            i.Limitations = CleanList(i.Limitations, 3, 200);
        }

        foreach (var c in d.Collaborations!)
        {
            c.PartnerName = Clean(c.PartnerName, 200);
            c.CulturalConnection = Clean(c.CulturalConnection, 400);
            c.Format = Clean(c.Format, 200);
            c.FitRationale = Clean(c.FitRationale, 500);
            c.TestIdea = Clean(c.TestIdea, 300);
            c.Assumptions = CleanList(c.Assumptions, 4, 200);
        }

        foreach (var c in d.Campaigns!)
        {
            c.Title = Clean(c.Title, 120);
            c.Objective = Clean(c.Objective, 200);
            c.TargetHypothesis = Clean(c.TargetHypothesis, 300);
            c.CulturalInsight = Clean(c.CulturalInsight, 400);
            c.Angle = Clean(c.Angle, 300);
            c.Execution = CleanList(c.Execution, 6, 200);
            c.SuccessMetric = Clean(c.SuccessMetric, 150);
            c.EffortCategory = c.EffortCategory?.Trim();
            c.Assumptions = CleanList(c.Assumptions, 4, 200);
        }

        foreach (var e in d.Experiments!)
        {
            e.Hypothesis = Clean(e.Hypothesis, 500);
            e.Segment = Clean(e.Segment, 300);
            e.ProposedCampaign = Clean(e.ProposedCampaign, 500);
            e.PrimaryMetric = Clean(e.PrimaryMetric, 200);
            e.SuccessThreshold = Clean(e.SuccessThreshold, 200);
            e.IfSuccess = Clean(e.IfSuccess, 300);
            e.IfFailure = Clean(e.IfFailure, 300);
        }

        foreach (var m in d.MentionedEntities!)
            m.Name = Clean(m.Name, 200);

        d.MentionedEntities = [.. d.MentionedEntities.Take(40)];
        d.Limitations = CleanList(d.Limitations, 8, 300);
    }

    internal static string? Clean(string? value, int maxLength)
    {
        if (value is null)
            return null;
        var stripped = Whitespace().Replace(HtmlTag().Replace(value, " "), " ").Trim();
        return stripped.Length <= maxLength ? stripped : stripped[..maxLength].TrimEnd();
    }

    private static List<string> CleanList(List<string>? values, int maxItems, int maxLength) =>
        [.. (values ?? []).Select(v => Clean(v, maxLength)).OfType<string>().Where(v => v.Length > 0).Take(maxItems)];

    private static int Len(string? value) => value?.Length ?? 0;

    private static string StripCodeFence(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? trimmed[(firstNewline + 1)..lastFence] : trimmed;
    }

    [GeneratedRegex("<[^>]{0,500}>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Rule 9: percentages, "N followers/likes/views/subscribers", and "engagement rate of N".</summary>
    [GeneratedRegex(@"(?<num>\d+(?:[.,]\d+)?)\s*%|(?<num>\d[\d,.]*\s*[kKmM]?)\s+(?:followers|likes|views|subscribers)|engagement rate[^\d]{0,20}(?<num>\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex NumericClaim();
}
