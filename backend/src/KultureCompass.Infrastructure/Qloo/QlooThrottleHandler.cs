using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Shared token-bucket limiter enforcing Qloo:MaxRequestsPerSecond across every call made through the
/// typed client. Must be registered as a singleton so the bucket is actually shared. Hackathon key observed at
/// ~5 rps; default config stays under that (BE-03 §6).</summary>
public sealed class QlooThrottleHandler : DelegatingHandler
{
    private readonly RateLimiter _limiter;

    public QlooThrottleHandler(IOptions<QlooOptions> options)
    {
        var permitsPerSecond = Math.Max(1, options.Value.MaxRequestsPerSecond);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitsPerSecond,
            TokensPerPeriod = permitsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 100,
            AutoReplenishment = true,
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException("Qloo rate limiter queue exceeded; too many concurrent requests.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}
