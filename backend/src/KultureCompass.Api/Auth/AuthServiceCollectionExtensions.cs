using System.Globalization;
using System.Threading.RateLimiting;
using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KultureCompass.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    public const string AuthRateLimitPolicy = "auth";
    public const string AuthRefreshRateLimitPolicy = "auth-refresh";
    public const string GenerationRateLimitPolicy = "generation";
    public const string PublicRateLimitPolicy = "public";
    public const string EvaluatorPolicy = "Evaluator";

    public static IServiceCollection AddKultureCompassAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<AuthCookies>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from IOptions<JwtOptions> (not read eagerly) so test/host overrides of Jwt:* always apply.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                o.MapInboundClaims = false; // keep "sub" as "sub"
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = SecurityStampValidation.ValidateAsync };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(EvaluatorPolicy, p => p.RequireClaim("role", "evaluator"));

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };
            o.AddPolicy(AuthRateLimitPolicy, http => PerMinute(http, PartitionKey.Ip, limits => limits.AuthPerMinute));
            o.AddPolicy(AuthRefreshRateLimitPolicy, http => PerMinute(http, PartitionKey.Ip, limits => limits.AuthRefreshPerMinute));
            o.AddPolicy(GenerationRateLimitPolicy, http => PerMinute(http, PartitionKey.UserOrIp, limits => limits.GenerationPerMinute));
            o.AddPolicy(PublicRateLimitPolicy, http => PerMinute(http, PartitionKey.Ip, limits => limits.PublicPerMinute));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/api/v1/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : PerMinute(http, PartitionKey.UserOrIp, limits => limits.GlobalPerMinute));
        });

        return services;
    }

    private enum PartitionKey
    {
        Ip,
        UserOrIp,
    }

    private static RateLimitPartition<string> PerMinute(HttpContext http, PartitionKey by, Func<RateLimitingOptions, int> permitLimit)
    {
        var limits = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = by == PartitionKey.UserOrIp ? http.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value : null;
        var key = user is not null ? $"user:{user}" : $"ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(limits),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
