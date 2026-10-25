using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo.Dtos;
using Polly.Timeout;

namespace KultureCompass.Infrastructure.Qloo;

/// <summary>Live Qloo client. Only uses request shapes documented as (at least) observed in BE-03 §1 — see
/// docs/backend/qloo-verified-contract.md for what still needs confirming against a real key.</summary>
public sealed class QlooClient(HttpClient httpClient) : IQlooClient
{
    public async Task<QlooResult<IReadOnlyList<QlooEntity>>> SearchEntitiesAsync(EntitySearchRequest request, CancellationToken ct)
    {
        if (!QlooQueryBuilder.TryBuildSearchQuery(request, out var path, out var error))
        {
            return QlooResult<IReadOnlyList<QlooEntity>>.Fail(QlooErrorKind.BadRequest, error!);
        }

        return await SendAsync<SearchEnvelopeDto, IReadOnlyList<QlooEntity>>(
            path, dto => QlooMapper.ToEntities(dto.Results), ct).ConfigureAwait(false);
    }

    public async Task<QlooResult<IReadOnlyList<QlooTag>>> SearchTagsAsync(string query, int take, CancellationToken ct)
    {
        if (!QlooQueryBuilder.TryBuildTagsQuery(query, take, out var path, out var error))
        {
            return QlooResult<IReadOnlyList<QlooTag>>.Fail(QlooErrorKind.BadRequest, error!);
        }

        return await SendAsync<TagsEnvelopeDto, IReadOnlyList<QlooTag>>(
            path, dto => QlooMapper.ToTags(dto.Results?.Tags), ct).ConfigureAwait(false);
    }

    public async Task<QlooResult<InsightsResponse>> GetInsightsAsync(InsightsRequest request, CancellationToken ct)
    {
        if (!QlooQueryBuilder.TryBuildInsightsQuery(request, out var path, out var error))
        {
            return QlooResult<InsightsResponse>.Fail(QlooErrorKind.BadRequest, error!);
        }

        return await SendAsync<InsightsEnvelopeDto, InsightsResponse>(
            path,
            dto => new InsightsResponse(QlooMapper.ToInsightsItems(dto.Results), QlooMapper.ToLocalities(dto.Query)),
            ct)
            .ConfigureAwait(false);
    }

    private async Task<QlooResult<TOut>> SendAsync<TDto, TOut>(string path, Func<TDto, TOut> map, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(path, ct).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException)
        {
            return QlooResult<TOut>.Fail(QlooErrorKind.Timeout, "The request to Qloo timed out.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return QlooResult<TOut>.Fail(QlooErrorKind.Timeout, "The request to Qloo timed out.");
        }
        catch (HttpRequestException)
        {
            return QlooResult<TOut>.Fail(QlooErrorKind.Unavailable, "Qloo is currently unavailable.");
        }

        using (response)
        {
            var meta = new QlooResultMeta(stopwatch.Elapsed, TryReadRateLimitRemaining(response));

            if (!response.IsSuccessStatusCode)
            {
                return QlooResult<TOut>.Fail(MapStatusToErrorKind(response.StatusCode), SafeMessageFor(response.StatusCode));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return QlooResult<TOut>.Fail(QlooErrorKind.InvalidResponse, "Qloo returned a non-JSON response.");
            }

            try
            {
                var dto = await response.Content.ReadFromJsonAsync<TDto>(QlooJson.Options, ct).ConfigureAwait(false);
                if (dto is null)
                {
                    return QlooResult<TOut>.Fail(QlooErrorKind.InvalidResponse, "Qloo returned an empty response body.");
                }

                return QlooResult<TOut>.Ok(map(dto), meta);
            }
            catch (JsonException)
            {
                return QlooResult<TOut>.Fail(QlooErrorKind.InvalidResponse, "Qloo returned a response that could not be parsed.");
            }
        }
    }

    private static QlooErrorKind MapStatusToErrorKind(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => QlooErrorKind.BadRequest,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => QlooErrorKind.AuthFailed,
        HttpStatusCode.TooManyRequests => QlooErrorKind.RateLimited,
        HttpStatusCode.RequestTimeout => QlooErrorKind.Timeout,
        _ => QlooErrorKind.Unavailable,
    };

    private static string SafeMessageFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Qloo rejected the request (invalid signal or filter).",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Qloo rejected the API key.",
        HttpStatusCode.TooManyRequests => "Qloo rate limit exceeded.",
        HttpStatusCode.RequestTimeout => "The request to Qloo timed out.",
        _ => "Qloo is currently unavailable.",
    };

    private static int? TryReadRateLimitRemaining(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-second-ratelimit-remaining", out var values)
        && int.TryParse(values.FirstOrDefault(), out var remaining)
            ? remaining
            : null;
}
