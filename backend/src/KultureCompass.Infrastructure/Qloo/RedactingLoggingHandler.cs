using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Logs method, path template, status, duration and attempt count at Information level — never headers,
/// never query string values, never the API key (02-security §7).</summary>
public sealed partial class RedactingLoggingHandler(ILogger<RedactingLoggingHandler> logger) : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<int> AttemptKey = new("kc-qloo-attempt");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Options.TryGetValue(AttemptKey, out var previousAttempt);
        var attempt = previousAttempt + 1;
        request.Options.Set(AttemptKey, attempt);

        var path = request.RequestUri?.AbsolutePath ?? "(unknown)";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            LogCompleted(logger, request.Method.Method, path, (int)response.StatusCode, attempt, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            LogFailed(logger, request.Method.Method, path, attempt, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Qloo {Method} {Path} -> {Status} (attempt {Attempt}, {ElapsedMs}ms)")]
    private static partial void LogCompleted(ILogger logger, string method, string path, int status, int attempt, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Qloo {Method} {Path} failed (attempt {Attempt}, {ElapsedMs}ms): {ExceptionType}")]
    private static partial void LogFailed(ILogger logger, string method, string path, int attempt, long elapsedMs, string exceptionType);
}
