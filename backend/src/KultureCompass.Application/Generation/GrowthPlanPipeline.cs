using System.Text.Json;
using System.Threading.Channels;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Research;
using KultureCompass.Domain.Evidence;
using KultureCompass.Domain.Plans;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KultureCompass.Application.Generation;

/// <summary>Config values the pipeline needs; bound by the Api from Generation/Qloo/Llm sections.</summary>
public sealed class GrowthPlanPipelineSettings
{
    public int MinEvidenceItems { get; set; } = 5;
    public int MaxQlooCallsPerPlan { get; set; } = 12;
    public int MaxOutputTokens { get; set; } = 6000;
    public int MaxRepairAttempts { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 180;
    public QlooMode QlooMode { get; set; } = QlooMode.Mock;
}

/// <summary>The fixed, bounded "agent" (BE-04 §1): 8 stages, typically 2 LLM calls, at most 2 + MaxRepairAttempts.
/// stage_log is persisted after every stage so the status endpoint shows real progress; every failure maps to a
/// <see cref="PlanErrorCode"/>. Never throws for plan-level problems — the plan ends Failed instead.</summary>
public sealed partial class GrowthPlanPipeline(
    IGrowthPlanStore store,
    ILlmClient llm,
    BriefInterpreter interpreter,
    ResearchPlanner planner,
    ICulturalIntelligenceService research,
    RecommendationSynthesizer synthesizer,
    IOptions<GrowthPlanPipelineSettings> settings,
    TimeProvider clock,
    ILogger<GrowthPlanPipeline> logger)
{
    public async Task RunAsync(Guid planId, CancellationToken ct)
    {
        var plan = await store.GetForProcessingAsync(planId, ct).ConfigureAwait(false);
        if (plan is null || plan.Status != GenerationStatus.Queued)
        {
            LogSkipped(logger, planId, plan?.Status);
            return;
        }

        var options = settings.Value;
        plan.Start(new RunContext(
            PromptLibrary.SchemaVersion, PromptLibrary.PromptVersion, options.QlooMode,
            Enum.TryParse<LlmProvider>(llm.ProviderName, out var provider) ? provider : LlmProvider.Mock,
            llm.Model, IsMock: llm.IsMock || options.QlooMode == QlooMode.Mock), Now());
        await store.SaveChangesAsync(ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var llmCalls = new List<LlmCallRecord>();

        try
        {
            var error = await RunStagesAsync(plan, options, llmCalls, timeout.Token).ConfigureAwait(false);
            if (error is not null)
                plan.Fail(error, Now());
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            plan.Fail(new PlanError(PlanErrorCode.Timeout, "Generation took longer than the allowed time. Please try again."), Now());
        }
        catch (QlooAuthFailedException)
        {
            plan.Fail(new PlanError(PlanErrorCode.QlooAuthFailed, "The cultural-intelligence provider rejected our credentials."), Now());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(logger, ex, planId);
            plan.Fail(new PlanError(PlanErrorCode.InternalError, "Something went wrong while generating this plan."), Now());
        }
        finally
        {
            if (plan.Status == GenerationStatus.Running && ct.IsCancellationRequested)
                LogInterrupted(logger, planId); // host shutdown: left Running for StuckPlanRecovery (BE-7)
        }

        // CancellationToken.None: a failure/timeout must still be persisted even though the run's token is cancelled.
        await store.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<PlanError?> RunStagesAsync(
        GrowthPlan plan, GrowthPlanPipelineSettings options, List<LlmCallRecord> llmCalls, CancellationToken ct)
    {
        var snapshot = plan.BriefSnapshot;

        // 1 — InterpretingBrief (call A; never fails the plan)
        var interpretation = await interpreter.InterpretAsync(snapshot, ct).ConfigureAwait(false);
        TrackCalls(plan, llmCalls, [interpretation.Call]);
        plan.SetInterpretation(JsonSerializer.Serialize(interpretation.Interpretation, PromptJson.Options), Now());
        if (interpretation.Limitation is not null)
            plan.AddLimitations([interpretation.Limitation], Now());
        await SaveAsync(ct).ConfigureAwait(false);

        // 2 — PlanningResearch (deterministic)
        var brief = new ResearchBrief(snapshot, interpretation.Interpretation);
        var researchPlan = planner.Plan(brief);
        plan.AdvanceTo(GenerationStage.PlanningResearch, $"{researchPlan.TotalOperations} Qloo requests planned", Now());
        plan.SetResearchPlan(JsonSerializer.Serialize(
            new { tier0 = researchPlan.Tier0.Select(o => o.Label), tier1 = researchPlan.Tier1.Select(o => o.Label) }, PromptJson.Options), Now());
        await SaveAsync(ct).ConfigureAwait(false);

        // 3 — RetrievingEvidence (Qloo, bounded by MaxQlooCallsPerPlan)
        var budget = Math.Min(options.MaxQlooCallsPerPlan, Math.Max(1, researchPlan.TotalOperations));
        plan.AdvanceTo(GenerationStage.RetrievingEvidence, $"0 of {researchPlan.TotalOperations} Qloo requests complete", Now());
        await SaveAsync(ct).ConfigureAwait(false);
        var evidenceSet = await RetrieveWithProgressAsync(plan, brief, new ResearchBudget(budget), ct).ConfigureAwait(false);
        plan.IncrementQlooCallCount(evidenceSet.CallsMade);
        plan.AttachEvidence(evidenceSet.Items, Now());
        plan.AddLimitations(evidenceSet.Limitations, Now());
        await SaveAsync(ct).ConfigureAwait(false);

        // 4 — EvaluatingEvidence (min-evidence gate)
        var evaluation = EvidenceEvaluator.Evaluate(evidenceSet, options.MinEvidenceItems);
        plan.AdvanceTo(GenerationStage.EvaluatingEvidence, EvidenceDetail(evaluation, evidenceSet.Items.Count), Now());
        await SaveAsync(ct).ConfigureAwait(false);
        if (!evaluation.HasSufficientEvidence)
            return new PlanError(PlanErrorCode.QlooNoEvidence, evaluation.InsufficientEvidenceMessage!);

        // 5 — GeneratingRecommendations (call B)
        var input = new SynthesisInput(snapshot, interpretation.Interpretation, evidenceSet.Items, plan.Limitations, plan.Options);
        var prompt = RecommendationSynthesizer.BuildPrompt(input);
        plan.AdvanceTo(GenerationStage.GeneratingRecommendations, $"Synthesising from {evidenceSet.Items.Count} evidence items", Now());
        await SaveAsync(ct).ConfigureAwait(false);
        var attempt = await synthesizer.GenerateAsync(prompt, options.MaxOutputTokens, ct).ConfigureAwait(false);
        TrackCalls(plan, llmCalls, [attempt.Call]);
        if (RecommendationSynthesizer.FatalError(attempt.Result) is { } fatal)
            return fatal;

        // 6 — ValidatingOutput (+ bounded repair calls)
        plan.AdvanceTo(GenerationStage.ValidatingOutput, null, Now());
        await SaveAsync(ct).ConfigureAwait(false);
        var validated = await synthesizer.ValidateAndRepairAsync(
            input, prompt, attempt.Result, options.MaxOutputTokens, options.MaxRepairAttempts, ct).ConfigureAwait(false);
        TrackCalls(plan, llmCalls, validated.RepairCalls);
        if (validated.Error is not null)
            return validated.Error;
        plan.AddLimitations(validated.Limitations, Now());
        plan.RecordDetail(validated.RepairCalls.Count == 0 ? "Output valid" : $"Output valid after {validated.RepairCalls.Count} repair attempt(s)", Now());
        await SaveAsync(ct).ConfigureAwait(false);

        // 7 — DesigningExperiments (deterministic)
        plan.AdvanceTo(GenerationStage.DesigningExperiments, null, Now());
        // Names the user wrote (their brand, their reference entities) are not "AI suggestions", even when the model
        // declares them without an evidence key (seen in live runs).
        var briefNames = BriefNames(snapshot);
        var content = DraftMapper.Map(validated.Draft!, evidenceSet.Items, [.. validated.UnlinkedMentions.Where(n => !briefNames.Contains(n))]);
        var experiments = ExperimentDesigner.Design(validated.Draft!, snapshot);
        await SaveAsync(ct).ConfigureAwait(false);

        // 8 — Saving
        plan.AdvanceTo(GenerationStage.Saving, null, Now());
        plan.AttachResults(content.Summary, content.Recommendations, experiments,
            WithoutRestatements(content.Limitations, plan.Limitations), content.UnlinkedMentions, Now());
        plan.Complete(Now());
        return null;
    }

    /// <summary>Qloo progress arrives from concurrent research tasks; a single consumer applies it to the plan and saves,
    /// so the DbContext is never touched from two threads.</summary>
    private async Task<EvidenceSet> RetrieveWithProgressAsync(GrowthPlan plan, ResearchBrief brief, ResearchBudget budget, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var consumer = Task.Run(async () =>
        {
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                string? latest = null;
                while (channel.Reader.TryRead(out var detail))
                    latest = detail; // coalesce bursts: only the newest counter matters
                plan.RecordDetail(latest!, Now());
                await store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }, ct);

        try
        {
            return await research.RetrieveAsync(brief, budget, new ChannelProgress(channel.Writer), ct).ConfigureAwait(false);
        }
        finally
        {
            channel.Writer.TryComplete();
            await consumer.ConfigureAwait(false);
        }
    }

    public static IReadOnlySet<string> BriefNames(BriefSnapshot snapshot) =>
        new HashSet<string>([snapshot.BrandName, .. snapshot.ReferenceEntities.Select(r => r.Name)], StringComparer.OrdinalIgnoreCase);

    /// <summary>Live runs showed the model echoing research limitations with small wording changes ("budget (12)
    /// reached" vs "budget reached"). Compare on letters only and keep the retrieved wording.</summary>
    internal static IReadOnlyList<string> WithoutRestatements(IReadOnlyList<string> generated, IReadOnlyList<string> existing)
    {
        static string Letters(string s) => new([.. s.Where(char.IsLetter).Select(char.ToLowerInvariant)]);
        var seen = existing.Select(Letters).ToHashSet(StringComparer.Ordinal);
        return [.. generated.Where(l => seen.Add(Letters(l)))];
    }

    private static string EvidenceDetail(EvidenceEvaluation evaluation, int total) =>
        $"{total} evidence items: {evaluation.CountsByQuality[EvidenceQuality.Strong]} strong, " +
        $"{evaluation.CountsByQuality[EvidenceQuality.Moderate]} moderate, {evaluation.CountsByQuality[EvidenceQuality.Weak]} weak";

    /// <summary>Counts and token usage are recorded as each call happens, so a plan that later fails still shows what
    /// it spent.</summary>
    private static void TrackCalls(GrowthPlan plan, List<LlmCallRecord> calls, IReadOnlyList<LlmCallRecord> newCalls)
    {
        if (newCalls.Count == 0)
            return;
        calls.AddRange(newCalls);
        plan.IncrementLlmCallCount(newCalls.Count);
        plan.SetLlmUsage(JsonSerializer.Serialize(new
        {
            calls,
            promptTokens = calls.Sum(c => c.Usage?.PromptTokens ?? 0),
            outputTokens = calls.Sum(c => c.Usage?.OutputTokens ?? 0),
        }, PromptJson.Options));
    }

    private Task SaveAsync(CancellationToken ct) => store.SaveChangesAsync(ct);

    private DateTimeOffset Now() => clock.GetUtcNow();

    private sealed class ChannelProgress(ChannelWriter<string> writer) : IProgress<string>
    {
        public void Report(string value) => writer.TryWrite(value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Plan {PlanId} skipped (status {Status}).")]
    private static partial void LogSkipped(ILogger logger, Guid planId, GenerationStatus? status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Plan {PlanId} failed with an unexpected error.")]
    private static partial void LogUnexpected(ILogger logger, Exception ex, Guid planId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Plan {PlanId} interrupted by shutdown; left Running for recovery.")]
    private static partial void LogInterrupted(ILogger logger, Guid planId);
}
