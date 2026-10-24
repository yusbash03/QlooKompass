using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace KultureCompass.Api.ErrorHandling;

/// <summary>
/// Maps unhandled exceptions to RFC 9457 Problem Details with the <c>code</c>/<c>traceId</c>
/// extension members used throughout contracts/openapi.yaml's ProblemDetails schema.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        Log.Error(exception, "Unhandled exception. TraceId={TraceId} Path={Path}", traceId, httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = environment.IsDevelopment() ? exception.Message : "An unexpected error occurred. Please try again later.",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = "internal_error";
        problem.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
