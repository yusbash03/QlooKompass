using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using KultureCompass.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace KultureCompass.Infrastructure.Llm;

/// <summary>Bounded retry policy shared by the LLM providers (BE-04 §2): 5xx retried ≤ 2 with backoff; a
/// non-quota 429 retried once honouring the provider's delay (capped); 400/401/403 never retried. Logs status,
/// duration and attempt only — never headers, keys or bodies (02-security §7).</summary>
internal sealed partial class LlmHttpSender(HttpClient http, ILogger logger, string provider, TimeSpan maxRetryDelay)
{
    private const int Max5xxRetries = 2;

    public sealed record Response(HttpStatusCode Status, string Body, TimeSpan? RetryAfter);

    /// <returns>The final HTTP response, or a failure for transport-level problems (timeout, network).</returns>
    public async Task<(Response? Response, LlmResult.Failure? Failure)> SendAsync(
        Func<HttpRequestMessage> createRequest, string model, Func<string, bool> isQuotaExhausted, CancellationToken ct)
    {
        var serverErrors = 0;
        var rateLimitRetried = false;

        for (var attempt = 1; ; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            Response response;
            try
            {
                using var request = createRequest();
                using var message = await http.SendAsync(request, ct).ConfigureAwait(false);
                var body = await message.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                response = new Response(message.StatusCode, body, message.Headers.RetryAfter?.Delta);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                LogFailed(logger, provider, model, attempt, stopwatch.ElapsedMilliseconds, "timeout");
                return (null, new LlmResult.Failure(LlmErrorKind.Timeout, $"{provider} did not respond in time."));
            }
            catch (HttpRequestException ex)
            {
                LogFailed(logger, provider, model, attempt, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
                if (++serverErrors <= Max5xxRetries)
                {
                    await Task.Delay(Backoff(serverErrors), ct).ConfigureAwait(false);
                    continue;
                }
                return (null, new LlmResult.Failure(LlmErrorKind.Unavailable, $"{provider} could not be reached."));
            }

            LogCompleted(logger, provider, model, (int)response.Status, attempt, stopwatch.ElapsedMilliseconds);

            if ((int)response.Status >= 500 && ++serverErrors <= Max5xxRetries)
            {
                await Task.Delay(Backoff(serverErrors), ct).ConfigureAwait(false);
                continue;
            }

            if (response.Status == HttpStatusCode.TooManyRequests && !rateLimitRetried && !isQuotaExhausted(response.Body))
            {
                rateLimitRetried = true;
                var delay = response.RetryAfter ?? ParseRetryDelay(response.Body) ?? TimeSpan.FromSeconds(2);
                await Task.Delay(delay < maxRetryDelay ? delay : maxRetryDelay, ct).ConfigureAwait(false);
                continue;
            }

            return (response, null);
        }
    }

    /// <summary>Maps a final non-2xx response. <paramref name="isQuotaExhausted"/> decides daily-quota vs. throttling.</summary>
    public static LlmResult.Failure MapError(Response response, string provider, Func<string, bool> isQuotaExhausted) => response.Status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(LlmErrorKind.AuthFailed, $"{provider} rejected the API key."),
        HttpStatusCode.TooManyRequests when isQuotaExhausted(response.Body) =>
            new(LlmErrorKind.QuotaExceeded, $"{provider} quota is exhausted."),
        HttpStatusCode.TooManyRequests =>
            new(LlmErrorKind.RateLimited, $"{provider} is rate limiting requests."),
        HttpStatusCode.BadRequest =>
            new(LlmErrorKind.Unavailable, $"{provider} rejected the request."),
        _ => new(LlmErrorKind.Unavailable, $"{provider} returned HTTP {(int)response.Status}."),
    };

    private static TimeSpan Backoff(int retry) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, retry - 1));

    /// <summary>Gemini puts it in the body: <c>"retryDelay": "21s"</c>.</summary>
    internal static TimeSpan? ParseRetryDelay(string body)
    {
        var match = RetryDelay().Match(body);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    [GeneratedRegex("\"retryDelay\"\\s*:\\s*\"([0-9.]+)s\"")]
    private static partial Regex RetryDelay();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} {Model} -> {Status} (attempt {Attempt}, {ElapsedMs}ms)")]
    private static partial void LogCompleted(ILogger logger, string provider, string model, int status, int attempt, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} {Model} failed (attempt {Attempt}, {ElapsedMs}ms): {Reason}")]
    private static partial void LogFailed(ILogger logger, string provider, string model, int attempt, long elapsedMs, string reason);
}
