using Microsoft.Extensions.Options;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Adds X-Api-Key from configuration. The key is never logged by this or any other handler in the
/// pipeline — RedactingLoggingHandler only logs method/path/status/duration/attempt.</summary>
public sealed class QlooApiKeyHandler(IOptions<QlooOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Api-Key");
        request.Headers.TryAddWithoutValidation("X-Api-Key", options.Value.ApiKey ?? string.Empty);
        return base.SendAsync(request, cancellationToken);
    }
}
