using FluentValidation;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;
using Microsoft.Extensions.Logging;

namespace KultureCompass.Application.Auth;

public sealed class RegisterHandler(
    IUserAccounts accounts, ITokenService tokens, IRefreshTokenStore refreshTokens, IValidator<RegisterRequest> validator)
{
    public async Task<Result<AuthSession>> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var created = await accounts.CreateAsync(request.Email!.Trim(), request.Password!, request.DisplayName?.Trim(), ct)
            .ConfigureAwait(false);

        switch (created.Status)
        {
            case CreateAccountStatus.EmailTaken:
                return AuthErrors.EmailTaken;
            case CreateAccountStatus.PasswordRejected:
                return AppError.Validation(new Dictionary<string, string[]> { ["password"] = [.. created.PasswordErrors] });
        }

        var user = created.Account!;
        var refresh = await refreshTokens.IssueNewFamilyAsync(user.Id, ct).ConfigureAwait(false);
        return Result<AuthSession>.Ok(AuthSession.Create(user, tokens.CreateAccessToken(user), refresh));
    }
}

public sealed class LoginHandler(
    IUserAccounts accounts, ITokenService tokens, IRefreshTokenStore refreshTokens, IValidator<LoginRequest> validator)
{
    public async Task<Result<AuthSession>> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validation.IsValid)
            return validation.ToAppError();

        var check = await accounts.CheckPasswordAsync(request.Email!.Trim(), request.Password!, ct).ConfigureAwait(false);
        if (check.Status == PasswordCheckStatus.LockedOut)
            return AuthErrors.AccountLocked;
        if (check.Status == PasswordCheckStatus.InvalidCredentials)
            return AuthErrors.InvalidCredentials;

        var user = check.Account!;
        var refresh = await refreshTokens.IssueNewFamilyAsync(user.Id, ct).ConfigureAwait(false);
        return Result<AuthSession>.Ok(AuthSession.Create(user, tokens.CreateAccessToken(user), refresh));
    }
}

public sealed partial class RefreshHandler(
    IUserAccounts accounts, ITokenService tokens, IRefreshTokenStore refreshTokens, ILogger<RefreshHandler> logger)
{
    public async Task<Result<AuthSession>> HandleAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return AuthErrors.RefreshInvalid;

        var rotation = await refreshTokens.RotateAsync(rawRefreshToken, ct).ConfigureAwait(false);
        switch (rotation.Status)
        {
            case RefreshRotationStatus.Reused:
                LogTokenReuse(logger, rotation.UserId);
                return AuthErrors.TokenReused;
            case RefreshRotationStatus.Invalid:
                return AuthErrors.RefreshInvalid;
        }

        var user = await accounts.FindByIdAsync(rotation.UserId!.Value, ct).ConfigureAwait(false);
        if (user is null)
            return AuthErrors.RefreshInvalid;

        return Result<AuthSession>.Ok(AuthSession.Create(user, tokens.CreateAccessToken(user), rotation.Replacement!));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoked refresh token reused; token family revoked. UserId={UserId}")]
    private static partial void LogTokenReuse(ILogger logger, Guid? userId);
}

public sealed class LogoutHandler(IRefreshTokenStore refreshTokens)
{
    public async Task<Result<Unit>> HandleAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
            await refreshTokens.RevokeFamilyAsync(rawRefreshToken, "logout", ct).ConfigureAwait(false);

        return Result<Unit>.Ok(Unit.Value);
    }
}

public sealed class MeHandler(ICurrentUser currentUser, IUserAccounts accounts)
{
    public async Task<Result<UserDto>> HandleAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(currentUser.OwnerId, out var userId))
            return AuthErrors.Unauthenticated;

        var user = await accounts.FindByIdAsync(userId, ct).ConfigureAwait(false);
        return user is null ? AuthErrors.Unauthenticated : Result<UserDto>.Ok(UserDto.From(user));
    }
}
