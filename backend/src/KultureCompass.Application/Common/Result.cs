namespace KultureCompass.Application.Common;

/// <summary>Maps 1:1 to an HTTP status in the Api's <c>ToHttpResult()</c> (BE-01 §3).</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    NotFound,
    Conflict,
    Locked,
    PreconditionFailed,
    PreconditionRequired,
    RateLimited,
    Unavailable,
}

/// <summary><paramref name="Code"/> is the contract's ProblemDetails <c>code</c> enum value
/// (docs/shared/01-api-contract.md §5). <paramref name="Message"/> must be user-safe. <paramref name="RetryAfterSeconds"/>
/// becomes a <c>Retry-After</c> header.</summary>
public sealed record AppError(
    string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string[]>? Errors = null, int? RetryAfterSeconds = null)
{
    public static AppError NotFound() => new("not_found", "The resource was not found.", ErrorKind.NotFound);

    public static AppError Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new("validation_failed", "One or more fields are invalid.", ErrorKind.Validation, errors);

    public static AppError Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });
}

public abstract record Result<T>
{
    private Result()
    {
    }

    public static Result<T> Ok(T value) => new Success(value);

    public static Result<T> Fail(AppError error) => new Failure(error);

    public static implicit operator Result<T>(AppError error) => new Failure(error);

    public sealed record Success(T Value) : Result<T>;

    public sealed record Failure(AppError Error) : Result<T>;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Return type for handlers whose success carries no payload (e.g. logout → 204).</summary>
public sealed record Unit
{
    public static readonly Unit Value = new();

    private Unit()
    {
    }
}
