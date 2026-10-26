using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>The process-wide token bucket for Qloo:MaxRequestsPerSecond (BE-03 §6; hackathon key observed at ~5 rps,
/// default config stays under that). A singleton, kept separate from <see cref="QlooThrottleHandler"/> because
/// IHttpClientFactory rebuilds handler chains every few minutes and must get fresh handler instances each time.</summary>
public sealed class QlooRateLimiter : IDisposable
{
    public QlooRateLimiter(IOptions<QlooOptions> options)
    {
        var permitsPerSecond = Math.Max(1, options.Value.MaxRequestsPerSecond);
        Limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerSecond,
            TokensPerPeriod = permitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 100,
            AutoReplenishment = true,
        });
    }

    public RateLimiter Limiter { get; }

    public void Dispose() => Limiter.Dispose();
}

/// <summary>Waits for a token from the shared <see cref="QlooRateLimiter"/> before every Qloo call. Register as
/// transient: DelegatingHandler instances must never be reused across handler chains.</summary>
public sealed class QlooThrottleHandler(QlooRateLimiter rateLimiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.Limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Qloo rate limiter queue exceeded; too many concurrent requests.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
