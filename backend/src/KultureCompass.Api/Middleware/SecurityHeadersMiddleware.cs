namespace KultureCompass.Api.Middleware;

/// <summary>02-security §3 headers on every response. The API serves JSON only, so CSP is <c>default-src 'none'</c> —
/// except the Development-only Scalar UI, which needs to load its own scripts and styles.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isDevDocsUi = environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/scalar");
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "DENY";
            if (!isDevDocsUi)
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
