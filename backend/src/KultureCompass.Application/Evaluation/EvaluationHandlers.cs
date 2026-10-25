using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Plans;
using KultureCompass.Domain.Evaluations;
using KultureCompass.Domain.Plans;
using Microsoft.Extensions.Logging;

namespace KultureCompass.Application.Evaluation;

public sealed record StartEvaluationRequest(Guid? BusinessProfileId);

public sealed record EvaluationDto(
    Guid Id, Guid BusinessProfileId, GenerationStatus Status, Guid? GroundedPlanId, JsonElement? LlmOnlyOutput,
    EvaluationMetrics? GroundedMetrics, EvaluationMetrics? LlmOnlyMetrics, IReadOnlyList<string> Notes, PlanErrorDto? Error,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt)
{
    public static EvaluationDto From(EvaluationRun run) => new(
        run.Id, run.BusinessProfileId, run.Status, run.GroundedPlanId,
        run.LlmOnlyOutputJson is null ? null : JsonDocument.Parse(run.LlmOnlyOutputJson).RootElement.Clone(),
        run.GroundedMetrics, run.LlmOnlyMetrics, run.Notes, PlanErrorDto.From(run.Error), run.CreatedAt, run.CompletedAt);
}

/// <summary>POST /evaluations (BE-04 §4). Reuses the normal plan start — so quota, ownership, one-active-plan and
/// idempotency rules all apply — with a plan idempotency key derived from the evaluation's key. A replayed POST finds
/// that plan and returns its evaluation; no extra schema is needed.</summary>
public sealed class StartEvaluationHandler(
    StartGrowthPlanHandler startPlan, IEvaluationStore evaluations, IGrowthPlanStore plans, EvaluationCompleter completer,
    ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<EvaluationDto>> HandleAsync(string? idempotencyKey, StartEvaluationRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return StartGrowthPlanHandler.KeyMissing;
        if (!Guid.TryParse(idempotencyKey, out var key))
            return AppError.Validation("Idempotency-Key", "Idempotency-Key must be a UUID.");

        var planKey = DerivePlanKey(key).ToString("D");
        var started = await startPlan.HandleAsync(
            new StartGrowthPlanCommand(planKey, new StartGrowthPlanRequest(body.BusinessProfileId, null)), ct).ConfigureAwait(false);
        if (started is Result<StartedPlan>.Failure failure)
            return failure.Error;

        var planId = ((Result<StartedPlan>.Success)started).Value.Plan.Id;
        if (await evaluations.GetByGroundedPlanIdAsync(planId, ct).ConfigureAwait(false) is { } existing)
            return Result<EvaluationDto>.Ok(EvaluationDto.From(existing));

        var run = EvaluationRun.CreateQueued(user.OwnerId, body.BusinessProfileId!.Value, clock.GetUtcNow());
        run.Start();
        run.AttachGroundedPlan(planId);
        await evaluations.AddAsync(run, ct).ConfigureAwait(false);
        await evaluations.SaveChangesAsync(ct).ConfigureAwait(false);

        // The worker can finish the plan before this row exists (its completer then finds nothing to do). Close that
        // race here: if the plan is already terminal, complete the evaluation inline.
        if ((await plans.GetStatusByIdAsync(planId, user.OwnerId, ct).ConfigureAwait(false))?.Status is GenerationStatus.Completed
            or GenerationStatus.CompletedWithLimitations or GenerationStatus.Failed)
        {
            await completer.CompleteForPlanAsync(planId, ct).ConfigureAwait(false);
        }

        return Result<EvaluationDto>.Ok(EvaluationDto.From(run));
    }

    /// <summary>Deterministic per evaluation key, distinct from any client-chosen plan key.</summary>
    internal static Guid DerivePlanKey(Guid evaluationKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"evaluation:{evaluationKey:D}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public sealed class GetEvaluationHandler(IEvaluationStore evaluations, ICurrentUser user)
{
    public async Task<Result<EvaluationDto>> HandleAsync(Guid id, CancellationToken ct)
    {
        var run = await evaluations.GetByIdAsync(id, user.OwnerId, ct).ConfigureAwait(false);
        return run is null ? AppError.NotFound() : Result<EvaluationDto>.Ok(EvaluationDto.From(run));
    }
}

/// <summary>Runs after the grounded plan reaches a terminal state (called by the worker): one LLM-only call with the
/// same brief, schema shape and counts but no evidence, then deterministic scoring of both.</summary>
public sealed partial class EvaluationCompleter(
    IEvaluationStore evaluations, IGrowthPlanStore plans, ILlmClient llm, TimeProvider clock, ILogger<EvaluationCompleter> logger)
{
    private const int MaxOutputTokens = 6000;

    public async Task CompleteForPlanAsync(Guid planId, CancellationToken ct)
    {
        var run = await evaluations.GetByGroundedPlanIdAsync(planId, ct).ConfigureAwait(false);
        if (run is null || run.Status != GenerationStatus.Running)
            return;

        var plan = await plans.GetForProcessingAsync(planId, ct).ConfigureAwait(false);
        if (plan is null || !plan.IsTerminal)
            return;

        if (plan.Status == GenerationStatus.Failed)
        {
            run.Fail(plan.Error ?? new PlanError(PlanErrorCode.InternalError, "The grounded plan failed."), clock.GetUtcNow());
            await evaluations.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var limits = new SynthesisInput(plan.BriefSnapshot, Research.BriefInterpretation.DeriveDeterministically(plan.BriefSnapshot), [], [], plan.Options).Limits;
        var user = $"<brief>{PromptJson.Brief(plan.BriefSnapshot)}</brief>\n<constraints>{{\"insights\":\"3-6\",\"collaborations\":\"0-{limits.MaxCollaborations}\",\"campaigns\":\"1-{limits.MaxCampaigns}\",\"experiments\":\"1 per campaign, max 6\",\"currency\":\"NGN\",\"schemaVersion\":\"1.0\"}}</constraints>";
        var result = await llm.GenerateJsonAsync(new LlmJsonRequest(
            PromptLibrary.LlmOnlySystem, user, PromptLibrary.LlmOnlySchema(), PromptLibrary.LlmOnlySchemaName, MaxOutputTokens), ct).ConfigureAwait(false);

        GrowthPlanDraft? draft = null;
        if (result is LlmResult.Success success)
        {
            try
            {
                draft = JsonSerializer.Deserialize<GrowthPlanDraft>(success.Json, GrowthPlanDraft.Json);
            }
            catch (JsonException)
            {
                draft = null;
            }
        }

        if (draft is null)
        {
            LogLlmOnlyFailed(logger, run.Id, (result as LlmResult.Failure)?.Kind.ToString() ?? "InvalidJson");
            run.Fail(new PlanError(PlanErrorCode.LlmOutputInvalid, "The LLM-only comparison output could not be produced."), clock.GetUtcNow());
            await evaluations.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var briefTerms = EvaluationScorer.BriefTerms(plan.BriefSnapshot);
        var briefJson = PromptJson.Brief(plan.BriefSnapshot);
        var briefNames = GrowthPlanPipeline.BriefNames(plan.BriefSnapshot);
        var grounded = EvaluationScorer.Score(EvaluationScorer.FromPlan(plan), [.. plan.Evidence.Select(e => e.Name)],
            briefJson + PromptJson.Evidence(plan.Evidence), briefTerms, briefNames);
        var llmOnly = EvaluationScorer.Score(EvaluationScorer.FromDraft(draft), [], briefJson, briefTerms, briefNames);

        run.Complete(JsonSerializer.Serialize(draft, GrowthPlanDraft.WriteJson), grounded, llmOnly, EvaluationScorer.Notes, clock.GetUtcNow());
        await evaluations.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation {EvaluationId}: LLM-only call failed ({Reason}).")]
    private static partial void LogLlmOnlyFailed(ILogger logger, Guid evaluationId, string reason);
}
