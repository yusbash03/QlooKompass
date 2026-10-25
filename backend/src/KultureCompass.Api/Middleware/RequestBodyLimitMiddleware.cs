using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KultureCompass.Api.Middleware;

/// <summary>02-security §6: rejects a declared Content-Length over App:MaxRequestBodyBytes with 413 before any body
/// is read (UseStatusCodePages turns it into Problem Details with code <c>payload_too_large</c>). Kestrel's
/// MaxRequestBodySize enforces the same limit for chunked bodies; this makes it host-independent and testable.</summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IOptions<AppOptions> options)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > options.Value.MaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
