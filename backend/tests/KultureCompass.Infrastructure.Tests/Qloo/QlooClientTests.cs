using System.Net;
using System.Net.Http.Headers;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Qloo;
using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

/// <summary>Exercises QlooClient through the real AddStandardResilienceHandler pipeline (fast timeouts/delays so
/// the retry tests stay quick) with a fake primary handler standing in for the network.</summary>
public class QlooClientTests
{
    private static readonly TimeSpan FastAttemptTimeout = TimeSpan.FromSeconds(1);

    private static IQlooClient BuildClient(FakeHttpMessageHandler fake, Action<HttpStandardResilienceOptions>? tweak = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient<IQlooClient, QlooClient>(client =>
            {
                client.BaseAddress = new Uri("https://example.test");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => fake)
            .AddStandardResilienceHandler(resilience =>
            {
                QlooServiceCollectionExtensions.ConfigureResilience(resilience, FastAttemptTimeout);
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(5);
                tweak?.Invoke(resilience);
            });

        return services.BuildServiceProvider().GetRequiredService<IQlooClient>();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SearchEntitiesAsync_maps_a_successful_response_to_entities()
    {
        var json = """{"results":[{"entity_id":"synthetic-artist-001","name":"Synthetic Afrobeat Star","types":["urn:entity:artist"],"popularity":0.82,"tags":[]}]}""";
        var fake = new FakeHttpMessageHandler().Enqueue(JsonResponse(HttpStatusCode.OK, json));
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "Burna Boy" }, CancellationToken.None);

        var success = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Success>();
        success.Value.Single().Name.ShouldBe("Synthetic Afrobeat Star");
        fake.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Unauthorized_maps_to_AuthFailed_and_is_not_retried()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        var failure = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Failure>();
        failure.Kind.ShouldBe(QlooErrorKind.AuthFailed);
        fake.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task BadRequest_maps_to_BadRequest_and_is_not_retried()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        var failure = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Failure>();
        failure.Kind.ShouldBe(QlooErrorKind.BadRequest);
        fake.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Missing_signal_is_rejected_locally_without_calling_Qloo()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildClient(fake);

        var result = await sut.GetInsightsAsync(new InsightsRequest { FilterType = QlooEntityType.Brand }, CancellationToken.None);

        var failure = result.ShouldBeOfType<QlooResult<InsightsResponse>.Failure>();
        failure.Kind.ShouldBe(QlooErrorKind.BadRequest);
        fake.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task TooManyRequests_honours_Retry_After_and_succeeds_on_the_next_attempt()
    {
        var retryAfterResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        retryAfterResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        var json = """{"results":[]}""";
        var fake = new FakeHttpMessageHandler()
            .Enqueue(retryAfterResponse)
            .Enqueue(JsonResponse(HttpStatusCode.OK, json));
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Success>();
        fake.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Persistent_5xx_is_retried_up_to_the_configured_max_then_maps_to_Unavailable()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        var failure = result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Failure>();
        failure.Kind.ShouldBe(QlooErrorKind.Unavailable);
        fake.CallCount.ShouldBe(3); // 1 initial attempt + MaxRetryAttempts(2)
    }

    [Fact]
    public async Task NonJson_content_type_maps_to_InvalidResponse()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", System.Text.Encoding.UTF8, "text/html"),
        });
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Failure>()
            .Kind.ShouldBe(QlooErrorKind.InvalidResponse);
    }

    [Fact]
    public async Task A_request_that_never_responds_within_the_attempt_timeout_maps_to_Timeout()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(async (_, ct) =>
        {
            await Task.Delay(FastAttemptTimeout * 3, ct); // longer than the attempt timeout; cancels with it
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sut = BuildClient(fake);

        var result = await sut.SearchEntitiesAsync(new EntitySearchRequest { Query = "x" }, CancellationToken.None);

        // Timeout is a transient outcome (BE-03 §3), so it is retried like a 5xx before ultimately failing —
        // don't assert an exact attempt count here, just that it eventually surfaces as Timeout.
        result.ShouldBeOfType<QlooResult<IReadOnlyList<QlooEntity>>.Failure>()
            .Kind.ShouldBe(QlooErrorKind.Timeout);
        fake.CallCount.ShouldBeGreaterThanOrEqualTo(1);
    }
}
