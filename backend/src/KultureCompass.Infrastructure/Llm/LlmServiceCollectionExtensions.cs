using KultureCompass.Application.Abstractions;
using KultureCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KultureCompass.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>Registers ILlmClient per <c>Llm:Provider</c> (Mock | Gemini | OpenAiCompatible). Retries are owned by the
    /// clients (BE-04 §2 semantics differ per status/body), so no resilience handler is added here.</summary>
    public static IServiceCollection AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        var llm = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var timeout = TimeSpan.FromSeconds(llm.TimeoutSeconds);

        switch (llm.Provider.ToUpperInvariant())
        {
            case "GEMINI":
                services.AddHttpClient<ILlmClient, GeminiLlmClient>(c => c.Timeout = timeout);
                break;
            case "OPENAICOMPATIBLE":
                services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>(c => c.Timeout = timeout);
                break;
            case "MOCK":
                services.AddSingleton<ILlmClient, MockLlmClient>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Llm:Provider '{llm.Provider}'. Use Mock, Gemini or OpenAiCompatible.");
        }

        return services;
    }
}
