namespace KultureCompass.Application.Abstractions;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user);

    /// <summary>Value placed in the <c>sstamp</c> claim: a hash of the Identity security stamp, never the raw stamp.</summary>
    string HashSecurityStamp(string securityStamp);
}
