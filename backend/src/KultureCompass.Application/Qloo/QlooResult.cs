namespace KultureCompass.Application.Qloo;

public enum QlooErrorKind
{
    BadRequest,
    AuthFailed,
    RateLimited,
    Unavailable,
    Timeout,
    InvalidResponse,
}

public sealed record QlooResultMeta(TimeSpan Duration, int? RateLimitRemaining);

/// <summary>Success/Failure union for every Qloo call. Callers pattern-match on <see cref="IsSuccess"/> or the
/// concrete <see cref="Success"/>/<see cref="Failure"/> record.</summary>
public abstract record QlooResult<T>
{
    private QlooResult()
    {
    }

    public bool IsSuccess => this is Success;

    public static QlooResult<T> Ok(T value, QlooResultMeta meta) => new Success(value, meta);

    public static QlooResult<T> Fail(QlooErrorKind kind, string safeMessage) => new Failure(kind, safeMessage);

    public sealed record Success(T Value, QlooResultMeta Meta) : QlooResult<T>;

    public sealed record Failure(QlooErrorKind Kind, string SafeMessage) : QlooResult<T>;
}
