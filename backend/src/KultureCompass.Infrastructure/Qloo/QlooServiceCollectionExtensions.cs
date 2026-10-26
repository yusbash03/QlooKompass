using KultureCompass.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Registers IQlooClient per Qloo:Mode. NOT yet called from Api/Program.cs — add
/// `services.AddQlooClient(builder.Configuration);` there once BE-2's DI wiring has landed, to avoid two forks
/// touching Program.cs/DependencyInjection.cs at once.</summary>
public static class QlooServiceCollectionExtensions
{
    public static IServiceCollection AddQlooClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QlooOptions>()
            .Bind(configuration.GetSection(QlooOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.Mode != QlooMode.Live || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Qloo:ApiKey is required when Qloo:Mode is Live (set it via user-secrets or env, never appsettings).")
            .ValidateOnStart();

        var qlooOptions = configuration.GetSection(QlooOptions.SectionName).Get<QlooOptions>() ?? new QlooOptions();

        if (qlooOptions.Mode == QlooMode.Mock)
        {
            services.AddSingleton<IQlooClient, MockQlooClient>();
            return services;
        }

        // Handlers MUST be transient: IHttpClientFactory rebuilds the handler chain every HandlerLifetime (2 min) and
        // throws if a DelegatingHandler instance is reused. Shared state (the rate limiter) lives in a singleton.
        services.AddSingleton<QlooRateLimiter>();
        services.AddTransient<QlooApiKeyHandler>();
        services.AddTransient<QlooThrottleHandler>();
        services.AddTransient<RedactingLoggingHandler>();

        var attemptTimeout = TimeSpan.FromSeconds(Math.Max(1, qlooOptions.TimeoutSeconds));

        services.AddHttpClient<IQlooClient, QlooClient>(client =>
            {
                client.BaseAddress = new Uri(qlooOptions.BaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience handler owns timeouts
            })
            .AddHttpMessageHandler<QlooApiKeyHandler>()
            .AddHttpMessageHandler<QlooThrottleHandler>()
            .AddHttpMessageHandler<RedactingLoggingHandler>()
            .AddStandardResilienceHandler(resilience => ConfigureResilience(resilience, attemptTimeout));

        return services;
    }

    internal static void ConfigureResilience(HttpStandardResilienceOptions resilience, TimeSpan attemptTimeout)
    {
        resilience.AttemptTimeout.Timeout = attemptTimeout;
        resilience.TotalRequestTimeout.Timeout = attemptTimeout * 3;
        resilience.Retry.MaxRetryAttempts = 2;
        resilience.Retry.UseJitter = true;
        resilience.Retry.BackoffType = DelayBackoffType.Exponential;
        resilience.Retry.ShouldHandle = args => ValueTask.FromResult(QlooResiliencePredicates.IsTransient(args.Outcome));
        resilience.Retry.DelayGenerator = QlooResiliencePredicates.RetryAfterAwareDelay;
        // The circuit breaker requires SamplingDuration >= 2 * AttemptTimeout; default to BE-03's documented 30s
        // unless a larger configured timeout forces a longer window.
        var minSamplingDuration = TimeSpan.FromSeconds(30);
        var requiredSamplingDuration = attemptTimeout * 2;
        resilience.CircuitBreaker.SamplingDuration = requiredSamplingDuration > minSamplingDuration
            ? requiredSamplingDuration
            : minSamplingDuration;
    }
}
