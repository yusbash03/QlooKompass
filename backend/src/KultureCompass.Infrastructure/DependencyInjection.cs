using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Persistence;
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

        return services;
    }
}
