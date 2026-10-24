using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Shared predicates for the standard resilience handler (BE-03 §3): retry only transient outcomes
/// (408/429/5xx/HttpRequestException/timeout — never 400/401/403/404), honour Retry-After when present.</summary>
internal static class QlooResiliencePredicates
{
    private static readonly TimeSpan MaxRetryAfterDelay = TimeSpan.FromSeconds(10);

    public static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TaskCanceledException or TimeoutException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Result is { } response)
        {
            var status = (int)response.StatusCode;
            return status == 408 || status == 429 || status >= 500;
        }

        return false;
    }

    public static ValueTask<TimeSpan?> RetryAfterAwareDelay(RetryDelayGeneratorArguments<HttpResponseMessage> args)
    {
        var retryAfter = args.Outcome.Result?.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        TimeSpan? delay = retryAfter.Delta
            ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        var capped = delay.Value > MaxRetryAfterDelay ? MaxRetryAfterDelay : delay.Value;
        return ValueTask.FromResult<TimeSpan?>(capped);
    }
}
