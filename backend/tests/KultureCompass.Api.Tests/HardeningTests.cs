using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace KultureCompass.Api.Tests;

/// <summary>Captures every Serilog event the app writes (registered as an ILogEventSink, which Program's
/// <c>ReadFrom.Services</c> picks up).</summary>
internal sealed class CapturingSink : ILogEventSink
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        var sb = new StringBuilder(logEvent.RenderMessage());
        foreach (var (key, value) in logEvent.Properties)
            sb.Append(' ').Append(key).Append('=').Append(value);
        if (logEvent.Exception is not null)
            sb.Append(' ').Append(logEvent.Exception);
        Lines.Enqueue(sb.ToString());
    }
}

[Collection(ApiCollection.Name)]
public class HardeningTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateApiClient();

    [Fact]
    public async Task Api_responses_carry_the_02_security_headers()
    {
        var response = await _client.GetAsync("/api/v1/meta");

        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task Request_bodies_over_64KB_are_rejected_with_413_payload_too_large()
    {
        var session = await _client.RegisterAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/business-profiles").WithBearer(session.AccessToken);
        request.Content = new StringContent($"{{\"brandName\":\"{new string('x', 70_000)}\"}}", Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("payload_too_large");
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_origin_with_credentials_and_never_a_wildcard()
    {
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/refresh");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "x-requested-with");
            return await _client.SendAsync(request);
        }

        var allowed = await Preflight("http://localhost:5173"); // App:BaseUrl
        var denied = await Preflight("https://evil.example");

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("http://localhost:5173");
        allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single().ShouldBe("true");
        denied.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Outside_Development_http_is_redirected_to_https_when_EnforceHttps_is_on()
    {
        var client = factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("App:EnforceHttps", "true");
                b.UseSetting("https_port", "443"); // deployments set ASPNETCORE_HTTPS_PORT; without a port no redirect happens
                b.UseSetting("Generation:RecoverOnStartup", "false");
            })
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect, HttpStatusCode.Redirect);
        response.Headers.Location!.Scheme.ShouldBe("https");
    }

    public static TheoryData<string, string?, Dictionary<string, string>> StartupGuards => new()
    {
        { "Qloo Live without key", null, new() { ["Qloo:Mode"] = "Live", ["Qloo:ApiKey"] = "" } },
        { "LLM provider without key", null, new() { ["Llm:Provider"] = "Gemini", ["Llm:ApiKey"] = "" } },
        { "Signing key too short", null, new() { ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[16]) } },
        { "Placeholder signing key in Production", "Production", new() { ["Jwt:SigningKey"] = "z7MfgP71HDHTQiNagjq5rfDbG/3New6hjqz7RqYMicDkQCtqR7QLGrJynJc5aGOb" } },
        { "Insecure refresh cookie in Production", "Production", new() { ["Jwt:CookieSecure"] = "false" } },
        { "Prompt logging in Production", "Production", new() { ["Llm:LogPrompts"] = "true" } },
    };

    [Theory]
    [MemberData(nameof(StartupGuards))]
    public void Misconfiguration_fails_fast_at_startup(string scenario, string? environment, Dictionary<string, string> settings)
    {
        var broken = factory.WithWebHostBuilder(b =>
        {
            if (environment is not null)
                b.UseEnvironment(environment);
            b.UseSetting("Generation:RecoverOnStartup", "false");
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });

        var ex = Should.Throw<Exception>(() => broken.CreateClient(), scenario);
        (ex is OptionsValidationException || ex.InnerException is OptionsValidationException || ex.ToString().Contains("OptionsValidationException"))
            .ShouldBeTrue(ex.ToString());
    }

    [Fact]
    public async Task Passwords_tokens_cookies_and_keys_never_appear_in_logs()
    {
        var sink = new CapturingSink();
        var client = factory.WithWebHostBuilder(b =>
            {
                b.UseSetting("Generation:RecoverOnStartup", "false");
                b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink));
            })
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Exercise every credential-bearing flow, including failures (which are the usual place secrets leak).
        var session = await client.RegisterAsync();
        await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Wrong-Synthetic-Passw0rd" });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = ApiTestExtensions.ValidPassword });
        var refreshed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh").WithRefreshCookie(session.RefreshCookie));
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh").WithRefreshCookie(session.RefreshCookie)); // reuse → warning log
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(session.AccessToken + "tampered"));
        var profile = new HttpRequestMessage(HttpMethod.Post, "/api/v1/business-profiles").WithBearer(session.AccessToken);
        profile.Content = JsonContent.Create(new { brandName = "Synthetic Brand", category = "Streetwear", productsDescription = "Synthetic products", marketingObjective = "Sales" });
        await client.SendAsync(profile);

        var secrets = new[]
        {
            ApiTestExtensions.ValidPassword,
            "Wrong-Synthetic-Passw0rd",
            session.AccessToken,
            session.RefreshCookie,
            login.GetRefreshCookieValue()!,
            refreshed.GetRefreshCookieValue() ?? "unused-placeholder-value",
            ApiFactory.SigningKey,
        };
        sink.Lines.ShouldNotBeEmpty();
        foreach (var line in sink.Lines)
        {
            foreach (var secret in secrets)
                line.ShouldNotContain(secret);
            line.ShouldNotContain("Bearer ", Case.Insensitive);
        }
    }
}
