using KultureCompass.Api.Auth;
using KultureCompass.Api.Http;
using KultureCompass.Application.Auth;

namespace KultureCompass.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest body, RegisterHandler handler, AuthCookies cookies, HttpResponse response, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult(s => cookies.SignedIn(response, s, StatusCodes.Status201Created)))
            .WithName("register")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimitPolicy)
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", async (LoginRequest body, LoginHandler handler, AuthCookies cookies, HttpResponse response, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttpResult(s => cookies.SignedIn(response, s, StatusCodes.Status200OK)))
            .WithName("login")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRateLimitPolicy)
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", async (RefreshHandler handler, AuthCookies cookies, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(cookies.Read(http.Request), ct);
                if (result is not Application.Common.Result<AuthSession>.Success)
                    cookies.Clear(http.Response);
                return result.ToHttpResult(s => cookies.SignedIn(http.Response, s, StatusCodes.Status200OK));
            })
            .WithName("refreshSession")
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceCollectionExtensions.AuthRefreshRateLimitPolicy)
            .AddEndpointFilter<RequireKcWebHeaderFilter>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", async (LogoutHandler handler, AuthCookies cookies, HttpContext http, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(cookies.Read(http.Request), ct);
                cookies.Clear(http.Response);
                return result.ToHttpResult(_ => TypedResults.NoContent());
            })
            .WithName("logout")
            .AddEndpointFilter<RequireKcWebHeaderFilter>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", async (MeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(TypedResults.Ok))
            .WithName("getCurrentUser")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
