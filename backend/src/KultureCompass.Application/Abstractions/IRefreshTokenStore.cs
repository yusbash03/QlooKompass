namespace KultureCompass.Application.Abstractions;

/// <summary>Raw token is only ever returned to the caller for the cookie; the store persists its SHA-256 hash.</summary>
public sealed record IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt);

public enum RefreshRotationStatus
{
    Rotated,
    /// <summary>Unknown, expired, or past its family's absolute lifetime.</summary>
    Invalid,
    /// <summary>An already-revoked token was presented: the whole family has been revoked (02-security §1).</summary>
    Reused,
}

public sealed record RefreshRotationResult(RefreshRotationStatus Status, Guid? UserId, IssuedRefreshToken? Replacement);

public interface IRefreshTokenStore
{
    Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, CancellationToken ct);

    Task<RefreshRotationResult> RotateAsync(string rawToken, CancellationToken ct);

    /// <summary>No-op when the token is unknown.</summary>
    Task RevokeFamilyAsync(string rawToken, string reason, CancellationToken ct);
}
