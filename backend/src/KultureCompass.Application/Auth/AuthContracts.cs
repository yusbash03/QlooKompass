using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Common;

namespace KultureCompass.Application.Auth;

/// <summary>Request fields are nullable so a missing field reaches the validator (400 with <c>errors</c>) instead
/// of failing JSON binding. Limits mirror contracts/openapi.yaml RegisterRequest/LoginRequest.</summary>
public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserDto(Guid Id, string Email, string? DisplayName, DateTimeOffset CreatedAt)
{
    public static UserDto From(UserAccount account) => new(account.Id, account.Email, account.DisplayName, account.CreatedAt);
}

public sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>What a successful register/login/refresh hands the Api: the JSON body plus the raw refresh token,
/// which the Api writes into the HttpOnly <c>kc_rt</c> cookie and never into a response body.</summary>
public sealed record AuthSession(AuthResponse Response, IssuedRefreshToken RefreshToken)
{
    public static AuthSession Create(UserAccount user, AccessToken access, IssuedRefreshToken refresh) =>
        new(new AuthResponse(access.Token, "Bearer", access.ExpiresAt, UserDto.From(user)), refresh);
}

public static class AuthErrors
{
    public static readonly AppError InvalidCredentials =
        new("invalid_credentials", "Email or password is incorrect.", ErrorKind.Unauthorized);

    public static readonly AppError AccountLocked =
        new("account_locked", "Too many failed sign-in attempts. Try again later.", ErrorKind.Locked);

    public static readonly AppError EmailTaken =
        new("email_taken", "An account with this email already exists.", ErrorKind.Conflict);

    public static readonly AppError RefreshInvalid =
        new("refresh_invalid", "Your session has expired. Please sign in again.", ErrorKind.Unauthorized);

    public static readonly AppError TokenReused =
        new("token_reused", "Your session was ended for security reasons. Please sign in again.", ErrorKind.Unauthorized);

    public static readonly AppError Unauthenticated =
        new("unauthenticated", "Authentication is required.", ErrorKind.Unauthorized);
}
