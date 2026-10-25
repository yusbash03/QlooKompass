using System.Text.Json;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using KultureCompass.Domain.Profiles;

namespace KultureCompass.Application.Generation;

public sealed record SynthesisInput(
    BriefSnapshot Snapshot,
    BriefInterpretation Interpretation,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<string> Limitations,
    GenerationOptions Options)
{
    public DraftLimits Limits => new(
        MaxCollaborations: Options.IncludeCollaborations ? 4 : 0,
        MaxCampaigns: Math.Clamp(Options.MaxCampaigns, DraftLimits.MinCampaigns, 3));
}

/// <summary>The rendered call-B prompt plus the grounding text the validator checks numbers against.</summary>
public sealed record SynthesisPrompt(string UserContent, string GroundingText);

public sealed record GenerationAttempt(LlmResult Result, LlmCallRecord Call);

public sealed record ValidatedDraft(
    GrowthPlanDraft? Draft,
    IReadOnlyList<string> UnlinkedMentions,
    IReadOnlyList<string> Limitations,
    PlanError? Error,
    IReadOnlyList<LlmCallRecord> RepairCalls);

/// <summary>Call B (synthesis) and the bounded validate → repair → drop loop (BE-04 §1 steps 5-6). At most
/// <c>maxRepairAttempts</c> extra LLM calls, never more.</summary>
public sealed class RecommendationSynthesizer(ILlmClient llm)
{
    public static string DroppedItemsLimitation(int count) =>
        $"{count} AI-generated item(s) were removed because they failed validation (for example, they referenced evidence that was not retrieved).";

    private const int MaxPreviousOutputChars = 24_000;

    public static SynthesisPrompt BuildPrompt(SynthesisInput input)
    {
        var briefJson = PromptJson.BriefWithInterpretation(input.Snapshot, input.Interpretation);
        var evidenceJson = PromptJson.Evidence(input.Evidence);
        var limits = input.Limits;

        var user = PromptLibrary.SynthUserTemplate
            .Replace("{{briefJson}}", briefJson, StringComparison.Ordinal)
            .Replace("{{evidenceJson}}", evidenceJson, StringComparison.Ordinal)
            .Replace("{{limitationsJson}}", PromptJson.Strings(input.Limitations), StringComparison.Ordinal)
            .Replace("{{collabRange}}", limits.MaxCollaborations == 0 ? "0" : $"0-{limits.MaxCollaborations}", StringComparison.Ordinal)
            .Replace("{{campaignRange}}", $"1-{limits.MaxCampaigns}", StringComparison.Ordinal)
            .Replace("{{budget}}", (input.Snapshot.BudgetRange ?? BudgetRange.NotSure).ToString(), StringComparison.Ordinal);

        return new SynthesisPrompt(user, briefJson + "\n" + evidenceJson);
    }

    public async Task<GenerationAttempt> GenerateAsync(SynthesisPrompt prompt, int maxOutputTokens, CancellationToken ct)
    {
        var result = await llm.GenerateJsonAsync(
            new LlmJsonRequest(PromptLibrary.SynthSystem, prompt.UserContent, PromptLibrary.GrowthPlanSchema(),
                PromptLibrary.GrowthPlanSchemaName, maxOutputTokens), ct).ConfigureAwait(false);
        return new(result, Record("synthesize", result));
    }

    /// <summary>Plan-level failure for a call-B provider error, or null when the output should go to validation
    /// (truncated/garbled output is repairable; quota/outage is not).</summary>
    public static PlanError? FatalError(LlmResult result) => result switch
    {
        LlmResult.Failure { Kind: LlmErrorKind.QuotaExceeded } =>
            new(PlanErrorCode.LlmQuotaExceeded, "The AI provider's quota is exhausted. Try again later."),
        LlmResult.Failure { Kind: LlmErrorKind.RateLimited or LlmErrorKind.Unavailable or LlmErrorKind.AuthFailed } =>
            new(PlanErrorCode.LlmUnavailable, "The AI provider is unavailable right now. Try again shortly."),
        LlmResult.Failure { Kind: LlmErrorKind.Timeout } =>
            new(PlanErrorCode.Timeout, "The AI provider took too long to respond."),
        LlmResult.Failure { Kind: LlmErrorKind.Refused } =>
            new(PlanErrorCode.LlmOutputInvalid, "The AI provider declined to produce a plan for this brief."),
        _ => null,
    };

    public async Task<ValidatedDraft> ValidateAndRepairAsync(
        SynthesisInput input, SynthesisPrompt prompt, LlmResult initial, int maxOutputTokens, int maxRepairAttempts,
        CancellationToken ct)
    {
        var limits = input.Limits;
        var repairCalls = new List<LlmCallRecord>();
        var (raw, validation) = Validate(initial, input, limits, prompt);

        for (var attempt = 1; !validation.IsValid && attempt <= maxRepairAttempts; attempt++)
        {
            var repairResult = await llm.GenerateJsonAsync(new LlmJsonRequest(
                PromptLibrary.SynthSystem + PromptLibrary.RepairAddendum,
                RepairUserContent(prompt, raw, validation.Errors),
                PromptLibrary.GrowthPlanSchema(),
                PromptLibrary.GrowthPlanSchemaName,
                maxOutputTokens,
                Temperature: 0.2), ct).ConfigureAwait(false);
            repairCalls.Add(Record($"repair-{attempt}", repairResult));

            // A provider failure during repair ends repairing; we fall through to the drop step with what we have.
            if (repairResult is LlmResult.Failure { Kind: not (LlmErrorKind.Truncated or LlmErrorKind.InvalidResponse) })
                break;

            var repaired = Validate(repairResult, input, limits, prompt);
            if (repaired.Validation.Draft is not null || validation.Draft is null)
                (raw, validation) = repaired;
        }

        if (validation.IsValid)
            return new(validation.Draft, validation.UnlinkedMentions, [], null, repairCalls);

        var documentErrors = validation.Errors.Where(e => e.ItemId is null && !e.Trimmable).ToList();
        if (validation.Draft is not null && documentErrors.Count == 0)
        {
            var trimmed = OutputValidator.DropInvalidItems(validation.Draft, validation.Errors, limits, out var dropped);
            var revalidated = OutputValidator.Validate(trimmed, input.Evidence, limits, prompt.GroundingText);
            if (revalidated.IsValid)
            {
                var limitation = dropped > 0 ? DroppedItemsLimitation(dropped) : null;
                return new(revalidated.Draft, revalidated.UnlinkedMentions, limitation is null ? [] : [limitation], null, repairCalls);
            }
        }

        return new(null, [], [], new PlanError(PlanErrorCode.LlmOutputInvalid,
            "The AI output could not be validated against the retrieved evidence, even after repair attempts."), repairCalls);
    }

    private static (string Raw, OutputValidationResult Validation) Validate(
        LlmResult result, SynthesisInput input, DraftLimits limits, SynthesisPrompt prompt) => result switch
        {
            LlmResult.Success s => (s.Json, OutputValidator.Validate(s.Json, input.Evidence, limits, prompt.GroundingText)),
            LlmResult.Failure { Kind: LlmErrorKind.Truncated } =>
                ("", new(null, [new(null, "Your output was cut off before it finished. Write shorter text so the complete JSON object fits.")], [], [])),
            LlmResult.Failure f => ("", new(null, [new(null, $"No usable output ({f.Kind}). Return the full JSON object.")], [], [])),
            _ => throw new InvalidOperationException("Unknown LLM result."),
        };

    private static string RepairUserContent(SynthesisPrompt prompt, string previousRaw, IReadOnlyList<Violation> violations)
    {
        var previous = previousRaw.Length > MaxPreviousOutputChars ? previousRaw[..MaxPreviousOutputChars] : previousRaw;
        return $"{prompt.UserContent}\n<previous_output>{previous}</previous_output>\n<violations>{JsonSerializer.Serialize(violations.Select(v => v.Message).Distinct().Take(30), PromptJson.Options)}</violations>";
    }

    private static LlmCallRecord Record(string purpose, LlmResult result) => result switch
    {
        LlmResult.Success s => new(purpose, true, s.Usage),
        LlmResult.Failure f => new(purpose, false, null, f.Kind.ToString()),
        _ => throw new InvalidOperationException("Unknown LLM result."),
    };
}
