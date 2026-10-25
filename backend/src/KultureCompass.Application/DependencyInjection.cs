using FluentValidation;
using KultureCompass.Application.Auth;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Plans;
using KultureCompass.Application.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace KultureCompass.Application;

public static class DependencyInjection
{
    /// <summary>Handlers + validators, registered explicitly (no assembly scanning, no MediatR).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddSingleton<IValidator<BusinessProfileRequest>, BusinessProfileRequestValidator>();
        services.AddSingleton<IValidator<PageQuery>, PageQueryValidator>();
        services.AddSingleton<IValidator<StartGrowthPlanRequest>, StartGrowthPlanRequestValidator>();

        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<MeHandler>();

        services.AddScoped<CreateProfileHandler>();
        services.AddScoped<GetProfileHandler>();
        services.AddScoped<ListProfilesHandler>();
        services.AddScoped<UpdateProfileHandler>();

        // Generation pipeline (BE-6). ResearchPlanner, ICulturalIntelligenceService and GrowthPlanPipelineSettings
        // depend on host config and are registered by the composition root.
        services.AddScoped<BriefInterpreter>();
        services.AddScoped<RecommendationSynthesizer>();
        services.AddScoped<GrowthPlanPipeline>();

        services.AddScoped<StartGrowthPlanHandler>();
        services.AddScoped<GetGrowthPlanHandler>();
        services.AddScoped<GetGrowthPlanStatusHandler>();
        services.AddScoped<ListGrowthPlansHandler>();
        services.AddScoped<GetQuotaHandler>();

        return services;
    }
}
