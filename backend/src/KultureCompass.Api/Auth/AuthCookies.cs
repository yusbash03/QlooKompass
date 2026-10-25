using KultureCompass.Application.Auth;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.Auth;

/// <summary>The <c>kc_rt</c> refresh cookie: HttpOnly; Secure (configurable for plain-http dev); SameSite=Strict;
/// Path=/api/v1/auth (02-security §1). The raw refresh token never appears in a response body.</summary>
public sealed class AuthCookies(IOptions<JwtOptions> jwt)
{
    public const string Name = "kc_rt";
    public const string Path = "/api/v1/auth";

    public string? Read(HttpRequest request) => request.Cookies[Name];

    public IResult SignedIn(HttpResponse response, AuthSession session, int statusCode)
    {
        response.Cookies.Append(Name, session.RefreshToken.RawToken, Options(session.RefreshToken.ExpiresAt));
        return TypedResults.Json(session.Response, statusCode: statusCode);
    }

    public void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(expires: null));

    private CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = jwt.Value.CookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
