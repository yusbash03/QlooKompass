using FluentValidation;
using KultureCompass.Application.Auth;
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

        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<MeHandler>();

        services.AddScoped<CreateProfileHandler>();
        services.AddScoped<GetProfileHandler>();
        services.AddScoped<ListProfilesHandler>();
        services.AddScoped<UpdateProfileHandler>();

        return services;
    }
}
