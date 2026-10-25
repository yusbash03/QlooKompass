using KultureCompass.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KultureCompass.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string OwnerId =>
        accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? throw new InvalidOperationException("ICurrentUser used outside an authenticated request.");
}
