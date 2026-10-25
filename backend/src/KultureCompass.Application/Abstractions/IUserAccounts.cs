namespace KultureCompass.Application.Abstractions;

public sealed record UserAccount(Guid Id, string Email, string? DisplayName, DateTimeOffset CreatedAt, string SecurityStamp);

public enum CreateAccountStatus
{
    Created,
    EmailTaken,
    PasswordRejected,
}

public sealed record CreateAccountResult(CreateAccountStatus Status, UserAccount? Account, IReadOnlyList<string> PasswordErrors);

public enum PasswordCheckStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record PasswordCheckResult(PasswordCheckStatus Status, UserAccount? Account);

/// <summary>Wraps ASP.NET Core Identity's UserManager/SignInManager so Application never hashes passwords or
/// touches Identity types (02-security §1).</summary>
public interface IUserAccounts
{
    Task<CreateAccountResult> CreateAsync(string email, string password, string? displayName, CancellationToken ct);

    /// <summary>Counts failures toward lockout. Unknown email and wrong password both return InvalidCredentials.</summary>
    Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken ct);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct);
}
