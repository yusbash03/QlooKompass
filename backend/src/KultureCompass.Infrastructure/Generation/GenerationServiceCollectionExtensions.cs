using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Application.Research;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DomainQlooMode = KultureCompass.Domain.Plans.QlooMode;

namespace KultureCompass.Infrastructure.Generation;

public static class GenerationServiceCollectionExtensions
{
    /// <summary>Builds the config-dependent research/generation services from Qloo, Generation and Llm options.</summary>
    public static IServiceCollection AddGenerationPipeline(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var qloo = sp.GetRequiredService<IOptions<QlooOptions>>().Value;
            return new ResearchPlanner(qloo.LocationInsightsVerified, qloo.PersonInsightsVerified, qloo.DemographicsVerified);
        });

        services.AddScoped<ICulturalIntelligenceService>(sp => new CulturalIntelligenceService(
            sp.GetRequiredService<IQlooClient>(),
            sp.GetRequiredService<ResearchPlanner>(),
            sp.GetRequiredService<IOptions<GenerationSettings>>().Value.MaxEvidenceItems,
            timeProvider: sp.GetRequiredService<TimeProvider>()));

        services.AddOptions<GrowthPlanPipelineSettings>()
            .Configure<IOptions<GenerationSettings>, IOptions<QlooOptions>, IOptions<LlmOptions>>((s, generation, qloo, llm) =>
            {
                s.MinEvidenceItems = generation.Value.MinEvidenceItems;
                s.TimeoutSeconds = generation.Value.TimeoutSeconds;
                s.MaxQlooCallsPerPlan = qloo.Value.MaxCallsPerPlan;
                s.MaxOutputTokens = llm.Value.MaxOutputTokens;
                s.MaxRepairAttempts = llm.Value.MaxRepairAttempts;
                s.QlooMode = qloo.Value.Mode == QlooMode.Mock ? DomainQlooMode.Mock : DomainQlooMode.Live;
            });

        return services;
    }
}
