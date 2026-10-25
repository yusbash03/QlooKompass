using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Identity;
using KultureCompass.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KultureCompass.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddScoped<IBusinessProfileStore, BusinessProfileStore>();
        services.AddScoped<IGrowthPlanStore, GrowthPlanStore>();
        services.AddScoped<IEvaluationStore, EvaluationStore>();

        // 02-security §1: min 10 chars (the "3 of 4 classes" rule is in RegisterRequestValidator), unique email,
        // lockout after 5 failures for 15 min.
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.User.RequireUniqueEmail = true;
                o.User.AllowedUserNameCharacters = string.Empty; // UserName = email; don't reject valid email chars
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<DemoUserSeeder>();

        return services;
    }
}
