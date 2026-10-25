using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Llm;
using KultureCompass.Infrastructure.Qloo;
using KultureCompass.Infrastructure.Tests.Qloo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests;

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{category} {formatter(state, exception)} {exception}";
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                line += " " + string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"));
            owner.Lines.Enqueue(line);
        }
    }
}

/// <summary>02-security §7: provider keys never reach logs — not in success paths, not in retries, not in failures.</summary>
public class LogRedactionTests
{
    private const string QlooKey = "synthetic-qloo-key-SHOULD-NEVER-BE-LOGGED";
    private const string LlmKey = "synthetic-llm-key-SHOULD-NEVER-BE-LOGGED";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Qloo_client_logs_status_and_path_but_never_the_api_key_or_query_values()
    {
        var logs = new CapturingLoggerProvider();
        var fake = new FakeHttpMessageHandler()
            .Enqueue(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"))
            .Enqueue(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(Options.Create(new QlooOptions { ApiKey = QlooKey, Mode = QlooMode.Live, BaseUrl = "https://example.test" }));
        services.AddSingleton<QlooApiKeyHandler>();
        services.AddSingleton<QlooThrottleHandler>();
        services.AddSingleton<RedactingLoggingHandler>();
        services.AddHttpClient<IQlooClient, QlooClient>(c => c.BaseAddress = new Uri("https://example.test"))
            .ConfigurePrimaryHttpMessageHandler(() => fake)
            .AddHttpMessageHandler<QlooApiKeyHandler>()
            .AddHttpMessageHandler<QlooThrottleHandler>()
            .AddHttpMessageHandler<RedactingLoggingHandler>()
            .AddStandardResilienceHandler(r =>
            {
                QlooServiceCollectionExtensions.ConfigureResilience(r, TimeSpan.FromSeconds(2));
                r.Retry.Delay = TimeSpan.FromMilliseconds(5);
            });
        var client = services.BuildServiceProvider().GetRequiredService<IQlooClient>();

        await client.SearchEntitiesAsync(new EntitySearchRequest { Query = "synthetic-private-query-term" }, CancellationToken.None);

        fake.Requests.ShouldAllBe(r => r.Headers.GetValues("X-Api-Key").Single() == QlooKey); // key really was sent…
        logs.Lines.ShouldNotBeEmpty();
        logs.Lines.ShouldAllBe(l => !l.Contains(QlooKey) && !l.Contains("synthetic-private-query-term")); // …but never logged
    }

    [Fact]
    public async Task Llm_clients_never_log_the_api_key_even_on_errors_and_retries()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var options = Options.Create(new LlmOptions { ApiKey = LlmKey, Model = "m", BaseUrl = "https://llm.example/v1", MaxRetryDelaySeconds = 0 });
        var request = new LlmJsonRequest("system", "user", JsonNode.Parse("""{"type":"object"}""")!, "redaction-test", 50);

        var geminiFake = new FakeHttpMessageHandler()
            .Enqueue(_ => Json(HttpStatusCode.InternalServerError, "{}"))
            .Enqueue(_ => Json(HttpStatusCode.TooManyRequests, """{"retryDelay":"1s"}"""))
            .Enqueue(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        await new GeminiLlmClient(new HttpClient(geminiFake), options, factory.CreateLogger<GeminiLlmClient>()).GenerateJsonAsync(request, CancellationToken.None);

        var openAiFake = new FakeHttpMessageHandler().Enqueue(_ => throw new HttpRequestException("synthetic network failure"));
        await new OpenAiCompatibleLlmClient(new HttpClient(openAiFake), options, factory.CreateLogger<OpenAiCompatibleLlmClient>())
            .GenerateJsonAsync(request, CancellationToken.None);

        logs.Lines.Count.ShouldBeGreaterThan(3);
        logs.Lines.ShouldAllBe(l => !l.Contains(LlmKey));
    }
}
