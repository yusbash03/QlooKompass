using System.Collections.Concurrent;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Evaluation;
using KultureCompass.Application.Generation;
using KultureCompass.Domain.Plans;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Generation;

/// <summary>Drains <see cref="ChannelGenerationQueue"/> with at most <c>Generation:WorkerConcurrency</c> (default 2)
/// pipelines in flight, each in its own DI scope (own DbContext). The pipeline enforces the per-plan timeout.</summary>
public sealed partial class GenerationWorker(
    ChannelGenerationQueue queue,
    IServiceScopeFactory scopes,
    IOptions<GenerationSettings> settings,
    ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.Value.RecoverOnStartup)
        {
            try
            {
                await StuckPlanRecovery.RunAsync(scopes, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecoveryFailed(logger, ex); // e.g. DB not reachable yet; new requests still flow
            }
        }

        using var gate = new SemaphoreSlim(settings.Value.WorkerConcurrency);
        var inFlight = new ConcurrentDictionary<Guid, Task>();

        try
        {
            await foreach (var planId in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await gate.WaitAsync(stoppingToken).ConfigureAwait(false);
                inFlight[planId] = Task.Run(async () =>
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<GrowthPlanPipeline>().RunAsync(planId, stoppingToken).ConfigureAwait(false);
                        // Evaluation mode (BE-8): if this plan is an evaluation's grounded run, do the LLM-only half now.
                        await scope.ServiceProvider.GetRequiredService<EvaluationCompleter>().CompleteForPlanAsync(planId, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogRunCrashed(logger, ex, planId);
                    }
                    finally
                    {
                        gate.Release();
                        inFlight.TryRemove(planId, out _);
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Let in-flight runs observe cancellation and persist; anything left Running is failed by recovery next start.
        await Task.WhenAll(inFlight.Values).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation run for plan {PlanId} crashed outside the pipeline.")]
    private static partial void LogRunCrashed(ILogger logger, Exception ex, Guid planId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup plan recovery failed.")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception ex);
}

/// <summary>Startup repair for the in-memory queue (BE-01 §4): Queued plans are re-enqueued; Running plans are failed
/// with <c>timeout</c>. The deployment is a single process, so a plan still marked Running at startup cannot have a
/// live worker any more (stricter than "older than the timeout", which would leave it blocking its profile).</summary>
public static partial class StuckPlanRecovery
{
    public const string InterruptedMessage = "Generation was interrupted by a server restart. Please try again.";

    public static async Task RunAsync(IServiceScopeFactory scopes, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IGrowthPlanStore>();
        var queue = services.GetRequiredService<IGenerationQueue>();
        var clock = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILogger<GenerationWorker>>();

        var unfinished = await store.GetUnfinishedAsync(ct).ConfigureAwait(false);
        var failed = 0;
        foreach (var plan in unfinished.Where(p => p.Status == GenerationStatus.Running))
        {
            plan.Fail(new PlanError(PlanErrorCode.Timeout, InterruptedMessage), clock.GetUtcNow());
            failed++;
        }
        await store.SaveChangesAsync(ct).ConfigureAwait(false);

        var queued = unfinished.Where(p => p.Status == GenerationStatus.Queued).ToList();
        foreach (var plan in queued)
            await queue.EnqueueAsync(plan.Id, ct).ConfigureAwait(false);

        LogRecovered(logger, queued.Count, failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Plan recovery: {Requeued} re-enqueued, {Failed} interrupted plans failed.")]
    private static partial void LogRecovered(ILogger logger, int requeued, int failed);
}
