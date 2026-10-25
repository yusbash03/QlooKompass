using KultureCompass.Application.Common;

namespace KultureCompass.Api.Http;

/// <summary>The one place a <see cref="Result{T}"/> becomes HTTP (BE-01 §3). Failures become RFC 9457 Problem Details
/// with the contract's <c>code</c> member; <c>traceId</c> is added centrally by ProblemDetailsOptions.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) => result switch
    {
        Result<T>.Success s => onSuccess(s.Value),
        Result<T>.Failure f => f.Error.ToProblem(),
        _ => throw new InvalidOperationException("Unknown result type."),
    };

    public static IResult ToProblem(this AppError error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error.Errors is not null)
        {
            return TypedResults.ValidationProblem(
                error.Errors.ToDictionary(kv => kv.Key, kv => kv.Value),
                detail: error.Message,
                extensions: extensions);
        }

        var problem = TypedResults.Problem(detail: error.Message, statusCode: StatusFor(error.Kind), extensions: extensions);
        return error.RetryAfterSeconds is { } seconds
            ? new HeaderResult(problem, "Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : problem;
    }

    internal static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorKind.Locked => StatusCodes.Status423Locked,
        ErrorKind.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Fallback <c>code</c> for problem responses produced by the framework rather than a handler (JWT
    /// challenge, model binding, rate limiter, unmatched route).</summary>
    internal static string? DefaultCodeFor(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => "validation_failed",
        StatusCodes.Status401Unauthorized => "unauthenticated",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        >= 500 => "internal_error",
        _ => null,
    };
}

/// <summary>Adds one response header, then executes the wrapped result.</summary>
public sealed class HeaderResult(IResult inner, string name, string value) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers[name] = value;
        return inner.ExecuteAsync(httpContext);
    }
}

/// <summary>Wraps a success result and adds <c>ETag: "&lt;version&gt;"</c> (01-api-contract §1 concurrency).</summary>
public sealed class ETagResult(IResult inner, string version) : IResult
{
    public static string Format(string version) => $"\"{version}\"";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.ETag = Format(version);
        return inner.ExecuteAsync(httpContext);
    }
}
