using System.Security.Cryptography;
using System.Text;
using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KultureCompass.Infrastructure.Identity;

/// <summary>HS256 access tokens via <see cref="JsonWebTokenHandler"/> (02-security §1). Claims: sub, jti, iat, nbf, exp,
/// iss, aud, email, sstamp.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public const string SecurityStampClaim = "sstamp";

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken CreateAccessToken(UserAccount user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateSigningKey(jwt), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [SecurityStampClaim] = HashSecurityStamp(user.SecurityStamp),
            },
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }

    public string HashSecurityStamp(string securityStamp) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp)));

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions jwt) =>
        new(Convert.FromBase64String(jwt.SigningKey));
}
