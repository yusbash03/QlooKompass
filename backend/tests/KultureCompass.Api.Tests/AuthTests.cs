using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KultureCompass.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KultureCompass.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateApiClient();

    private Task<HttpResponseMessage> GetMeAsync(string token) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(token));

    private Task<HttpResponseMessage> RefreshAsync(string? cookie, bool csrfHeader = true) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh").WithRefreshCookie(cookie, csrfHeader));

    [Fact]
    public async Task Register_returns_201_with_hardened_refresh_cookie_and_token_works_on_me()
    {
        var email = ApiTestExtensions.NewEmail();
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = ApiTestExtensions.ValidPassword, displayName = "Synthetic Founder" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cookie = response.GetRefreshCookieHeader()!.ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth");

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(ApiTestExtensions.ValidPassword);
        raw.ShouldNotContain("passwordHash", Case.Insensitive);
        raw.ShouldNotContain(response.GetRefreshCookieValue()!); // refresh token only ever in the cookie

        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        var me = await GetMeAsync(body.GetProperty("accessToken").GetString()!);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        meBody.GetProperty("email").GetString().ShouldBe(email);
        meBody.GetProperty("displayName").GetString().ShouldBe("Synthetic Founder");
    }

    [Fact]
    public async Task Access_token_carries_the_documented_claims_and_15_minute_lifetime()
    {
        var session = await _client.RegisterAsync();
        var jwt = new JsonWebToken(session.AccessToken);

        jwt.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
        jwt.Subject.ShouldBe(session.UserId.ToString());
        jwt.Issuer.ShouldBe(ApiFactory.Issuer);
        jwt.Audiences.ShouldBe([ApiFactory.Audience]);
        jwt.GetClaim("email").Value.ShouldBe(session.Email);
        jwt.TryGetClaim("jti", out _).ShouldBeTrue();
        jwt.TryGetClaim("sstamp", out _).ShouldBeTrue();
        (jwt.ValidTo - jwt.IssuedAt).ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409_email_taken()
    {
        var session = await _client.RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = session.Email, password = ApiTestExtensions.ValidPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("email_taken");
    }

    [Theory]
    [InlineData("short1A!")]          // < 10 chars
    [InlineData("alllowercaseletters")] // only 1 character class
    public async Task Register_with_weak_password_returns_400_validation_failed(string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = ApiTestExtensions.NewEmail(), password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty("password", out _).ShouldBeTrue();
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_return_the_same_401_invalid_credentials()
    {
        var session = await _client.RegisterAsync();

        var wrongPassword = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Wrong-Passw0rd" });
        var unknownEmail = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = ApiTestExtensions.NewEmail(), password = "Wrong-Passw0rd" });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await wrongPassword.ReadProblemAsync();
        var b = await unknownEmail.ReadProblemAsync();
        a.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        b.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        a.GetProperty("detail").GetString().ShouldBe(b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_succeeds_with_correct_password()
    {
        var session = await _client.RegisterAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = ApiTestExtensions.ValidPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.GetRefreshCookieValue().ShouldNotBeNull();
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_with_423_even_for_the_right_password()
    {
        var session = await _client.RegisterAsync();

        for (var i = 1; i <= 4; i++)
        {
            var failed = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Wrong-Passw0rd" });
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var fifth = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Wrong-Passw0rd" });
        fifth.StatusCode.ShouldBe(HttpStatusCode.Locked);
        (await fifth.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("account_locked");

        var correct = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = ApiTestExtensions.ValidPassword });
        correct.StatusCode.ShouldBe(HttpStatusCode.Locked);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_the_old_token_revokes_the_whole_family()
    {
        var session = await _client.RegisterAsync();

        var first = await RefreshAsync(session.RefreshCookie);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = first.GetRefreshCookieValue()!;
        rotated.ShouldNotBe(session.RefreshCookie);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();

        var reuse = await RefreshAsync(session.RefreshCookie);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await reuse.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("token_reused");
        reuse.GetRefreshCookieHeader().ShouldNotBeNull(); // cookie cleared

        // The legitimately rotated token died with its family.
        var afterTheft = await RefreshAsync(rotated);
        afterTheft.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_without_csrf_header_is_rejected_with_400()
    {
        var session = await _client.RegisterAsync();

        var response = await RefreshAsync(session.RefreshCookie, csrfHeader: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("validation_failed");
        // Rejected before rotation: the original token still works.
        (await RefreshAsync(session.RefreshCookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-refresh-token")]
    public async Task Refresh_with_missing_or_unknown_cookie_returns_401_refresh_invalid(string? cookie)
    {
        var response = await RefreshAsync(cookie);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("refresh_invalid");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_family_and_clears_the_cookie()
    {
        var session = await _client.RegisterAsync();

        var logout = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            .WithBearer(session.AccessToken).WithRefreshCookie(session.RefreshCookie));

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logout.GetRefreshCookieHeader().ShouldNotBeNull();
        logout.GetRefreshCookieValue().ShouldBeNull();
        (await RefreshAsync(session.RefreshCookie)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_requires_an_access_token()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout").WithRefreshCookie("x"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401_problem_unauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().ShouldBe("unauthenticated");
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    public enum TokenDefect
    {
        None,
        Expired,
        TamperedPayload,
        WrongIssuer,
        WrongAudience,
        WrongKey,
        AlgNone,
        Hs512,
    }

    [Theory]
    [InlineData(TokenDefect.None, HttpStatusCode.OK)] // control: the crafting itself yields an accepted token
    [InlineData(TokenDefect.Expired, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.TamperedPayload, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.WrongIssuer, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.WrongAudience, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.WrongKey, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.AlgNone, HttpStatusCode.Unauthorized)]
    [InlineData(TokenDefect.Hs512, HttpStatusCode.Unauthorized)]
    public async Task Crafted_tokens_are_only_accepted_when_fully_valid(TokenDefect defect, HttpStatusCode expected)
    {
        var session = await _client.RegisterAsync();
        var real = new JsonWebToken(session.AccessToken);
        var token = Craft(defect, real.Subject, real.GetClaim("sstamp").Value);

        (await GetMeAsync(token)).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Changing_the_security_stamp_invalidates_existing_access_tokens()
    {
        var session = await _client.RegisterAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(session.UserId.ToString());
            await users.UpdateSecurityStampAsync(user!);
        }

        (await GetMeAsync(session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_and_meta_are_public_and_meta_reveals_no_secrets()
    {
        (await _client.GetAsync("/api/v1/health")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var meta = await _client.GetAsync("/api/v1/meta");
        meta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await meta.Content.ReadAsStringAsync();
        raw.ShouldNotContain(ApiFactory.SigningKey);
        raw.ShouldNotContain("apiKey", Case.Insensitive);
        raw.ShouldNotContain("signingKey", Case.Insensitive);
    }

    private static string Craft(TokenDefect defect, string sub, string sstamp)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["sstamp"] = sstamp, ["jti"] = Guid.NewGuid().ToString() };

        if (defect == TokenDefect.AlgNone)
        {
            static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));
            var exp = new DateTimeOffset(now.AddMinutes(10)).ToUnixTimeSeconds();
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>(claims)
            {
                ["iss"] = ApiFactory.Issuer, ["aud"] = ApiFactory.Audience, ["exp"] = exp, ["nbf"] = exp - 600,
            });
            return $"{B64("""{"alg":"none","typ":"JWT"}""")}.{B64(payload)}.";
        }

        var key = defect == TokenDefect.WrongKey
            ? new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
            : new SymmetricSecurityKey(Convert.FromBase64String(ApiFactory.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = defect == TokenDefect.WrongIssuer ? "https://evil.example" : ApiFactory.Issuer,
            Audience = defect == TokenDefect.WrongAudience ? "someone-else" : ApiFactory.Audience,
            IssuedAt = defect == TokenDefect.Expired ? now.AddHours(-2) : now,
            NotBefore = defect == TokenDefect.Expired ? now.AddHours(-2) : now,
            Expires = defect == TokenDefect.Expired ? now.AddHours(-1) : now.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key,
                defect == TokenDefect.Hs512 ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256),
        };
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);

        if (defect == TokenDefect.TamperedPayload)
        {
            var parts = token.Split('.');
            var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]))
                .Replace(sub, Guid.NewGuid().ToString(), StringComparison.Ordinal);
            token = $"{parts[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}.{parts[2]}";
        }

        return token;
    }
}
