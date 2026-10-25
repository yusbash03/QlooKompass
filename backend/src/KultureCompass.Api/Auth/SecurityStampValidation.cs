using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KultureCompass.Api.Auth;

/// <summary>JwtBearer <c>OnTokenValidated</c>: rejects access tokens whose <c>sstamp</c> no longer matches the user's
/// current security stamp (e.g. after a password change), or whose user was deleted. The current stamp hash is
/// cached per user for 5 minutes, so invalidation takes effect within that window (02-security §1).</summary>
public static class SecurityStampValidation
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var presented = principal?.FindFirst(JwtTokenService.SecurityStampClaim)?.Value;
        if (!Guid.TryParse(sub, out var userId) || string.IsNullOrEmpty(presented))
        {
            context.Fail("Token is missing sub or sstamp.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();
        var cacheKey = $"sstamp:{userId}";

        if (!cache.TryGetValue(cacheKey, out string? current))
        {
            var user = await services.GetRequiredService<IUserAccounts>()
                .FindByIdAsync(userId, context.HttpContext.RequestAborted);
            current = user is null ? string.Empty : services.GetRequiredService<ITokenService>().HashSecurityStamp(user.SecurityStamp);
            cache.Set(cacheKey, current, CacheDuration);
        }

        if (!string.Equals(current, presented, StringComparison.Ordinal))
            context.Fail("Security stamp no longer valid.");
    }
}
