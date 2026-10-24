using KultureCompass.Application.Qloo;

namespace KultureCompass.Application.Abstractions;

/// <summary>Provider-neutral Qloo API surface (BE-03 §3). Implementations: QlooClient (live, verified-contract
/// only) and MockQlooClient (synthetic fixtures). The LLM never calls this directly — only
/// ICulturalIntelligenceService (BE-4) does.</summary>
public interface IQlooClient
{
    Task<QlooResult<IReadOnlyList<QlooEntity>>> SearchEntitiesAsync(EntitySearchRequest request, CancellationToken ct);

    Task<QlooResult<IReadOnlyList<QlooTag>>> SearchTagsAsync(string query, int take, CancellationToken ct);

    Task<QlooResult<InsightsResponse>> GetInsightsAsync(InsightsRequest request, CancellationToken ct);
}
