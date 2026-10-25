using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KultureCompass.Api;
using KultureCompass.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace KultureCompass.Api.Tests;

/// <summary>Real app (real JWT issuance/validation, real Identity, real EF Core) over a Testcontainers Postgres.
/// Uses a freshly generated test signing key and the "Testing" environment, so user-secrets (real Qloo key) are
/// never loaded and Qloo runs in Mock mode.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://kc-tests.local";
    public const string Audience = "kc-tests";

    // 64 bytes so AuthTests can also sign an HS512 token with the *real* key and prove HS512 is still rejected.
    public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("kulture_compass_api_test")
        .WithUsername("kc")
        .WithPassword("kc")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:CookieSecure", "true");
        builder.UseSetting("Qloo:Mode", "Mock");
        builder.UseSetting("Llm:Provider", "Mock");
        builder.UseSetting("RateLimiting:AuthPerMinute", "10000");
        builder.UseSetting("RateLimiting:AuthRefreshPerMinute", "10000");
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>No automatic cookie handling: tests pass kc_rt explicitly so rotation/reuse is unambiguous.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}

public sealed record TestSession(string AccessToken, string RefreshCookie, Guid UserId, string Email);

public static class ApiTestExtensions
{
    public const string ValidPassword = "Synthetic-Passw0rd";

    public static string NewEmail() => $"synthetic-{Guid.NewGuid():N}@example.test";

    public static async Task<TestSession> RegisterAsync(this HttpClient client, string? email = null)
    {
        email ??= NewEmail();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = ValidPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestSession(
            body.GetProperty("accessToken").GetString()!,
            response.GetRefreshCookieValue()!,
            body.GetProperty("user").GetProperty("id").GetGuid(),
            email);
    }

    public static string? GetRefreshCookieHeader(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("kc_rt=", StringComparison.Ordinal))
            : null;

    public static string? GetRefreshCookieValue(this HttpResponseMessage response)
    {
        var header = response.GetRefreshCookieHeader();
        if (header is null)
            return null;
        var value = header["kc_rt=".Length..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    public static HttpRequestMessage WithRefreshCookie(this HttpRequestMessage request, string? cookie, bool csrfHeader = true)
    {
        if (cookie is not null)
            request.Headers.Add("Cookie", $"kc_rt={cookie}");
        if (csrfHeader)
            request.Headers.Add("X-Requested-With", "kc-web");
        return request;
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
