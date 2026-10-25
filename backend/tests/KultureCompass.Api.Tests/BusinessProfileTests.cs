using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KultureCompass.Api.Tests;

[Collection(ApiCollection.Name)]
public class BusinessProfileTests(ApiFactory factory)
{
    private const string Path = "/api/v1/business-profiles";
    private readonly HttpClient _client = factory.CreateApiClient();

    private static object ValidBody(string brandName = "Synthetic Eko Threads") => new
    {
        brandName,
        description = "A synthetic Lagos streetwear label.",
        category = "Streetwear",
        productsDescription = "Graphic tees and caps",
        targetMarket = "Lagos 18-30",
        primaryLocation = "Lagos, Nigeria",
        marketingObjective = "Sales",
        referenceEntities = new[] { new { name = "Synthetic Afrobeat Star", kindHint = "Artist" } },
        budgetRange = "From100kTo500k",
    };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url).WithBearer(token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(request);
    }

    private async Task<(Guid Id, string ETag)> CreateAsync(string token, string brandName = "Synthetic Eko Threads")
    {
        var response = await SendAsync(HttpMethod.Post, Path, token, ValidBody(brandName));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Unauthenticated_requests_return_401()
    {
        (await _client.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync(Path, ValidBody())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_returns_201_with_Location_ETag_and_the_saved_profile()
    {
        var session = await _client.RegisterAsync();

        var response = await SendAsync(HttpMethod.Post, Path, session.AccessToken, ValidBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        response.Headers.Location!.OriginalString.ShouldBe($"{Path}/{id}");
        response.Headers.ETag!.Tag.ShouldBe($"\"{body.GetProperty("version").GetString()}\"");
        body.GetProperty("category").GetString().ShouldBe("Streetwear");
        body.GetProperty("referenceEntities")[0].GetProperty("kindHint").GetString().ShouldBe("Artist");
        body.TryGetProperty("ownerId", out _).ShouldBeFalse();

        var get = await SendAsync(HttpMethod.Get, $"{Path}/{id}", session.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        get.Headers.ETag!.Tag.ShouldBe(response.Headers.ETag.Tag);
        (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("brandName").GetString().ShouldBe("Synthetic Eko Threads");
    }

    [Fact]
    public async Task Another_owners_profile_is_404_for_get_and_put()
    {
        var owner = await _client.RegisterAsync();
        var intruder = await _client.RegisterAsync();
        var (id, etag) = await CreateAsync(owner.AccessToken);

        var get = await SendAsync(HttpMethod.Get, $"{Path}/{id}", intruder.AccessToken);
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await get.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("not_found");

        var put = await SendAsync(HttpMethod.Put, $"{Path}/{id}", intruder.AccessToken, ValidBody("Synthetic Hijack"), etag);
        put.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Missing_and_out_of_range_fields_return_400_problem_with_field_errors()
    {
        var session = await _client.RegisterAsync();

        var response = await SendAsync(HttpMethod.Post, Path, session.AccessToken, new
        {
            brandName = "X",
            category = "Streetwear",
            productsDescription = "short",
            referenceEntities = Enumerable.Range(0, 9).Select(i => new { name = $"Synthetic {i}", kindHint = "Brand" }),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.TryGetProperty("traceId", out _).ShouldBeTrue();
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("brandName", out _).ShouldBeTrue();
        errors.TryGetProperty("productsDescription", out _).ShouldBeTrue();
        errors.TryGetProperty("marketingObjective", out _).ShouldBeTrue();
        errors.TryGetProperty("referenceEntities", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Category_Other_requires_categoryOther()
    {
        var session = await _client.RegisterAsync();

        var response = await SendAsync(HttpMethod.Post, Path, session.AccessToken, new
        {
            brandName = "Synthetic Brand",
            category = "Other",
            productsDescription = "Handmade synthetic goods",
            marketingObjective = "Awareness",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("errors").TryGetProperty("categoryOther", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_enum_value_or_malformed_json_returns_400_not_500()
    {
        var session = await _client.RegisterAsync();

        var badEnum = new HttpRequestMessage(HttpMethod.Post, Path).WithBearer(session.AccessToken);
        badEnum.Content = new StringContent(
            """{"brandName":"Synthetic Brand","category":"Spaceships","productsDescription":"Synthetic products","marketingObjective":"Sales"}""",
            Encoding.UTF8, "application/json");
        var malformed = new HttpRequestMessage(HttpMethod.Post, Path).WithBearer(session.AccessToken);
        malformed.Content = new StringContent("{not json", Encoding.UTF8, "application/json");

        foreach (var request in new[] { badEnum, malformed })
        {
            var response = await _client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("validation_failed");
        }
    }

    [Fact]
    public async Task Put_enforces_If_Match_428_when_missing_412_when_stale_200_when_current()
    {
        var session = await _client.RegisterAsync();
        var (id, etag) = await CreateAsync(session.AccessToken);

        var missing = await SendAsync(HttpMethod.Put, $"{Path}/{id}", session.AccessToken, ValidBody("Synthetic Renamed"));
        missing.StatusCode.ShouldBe((HttpStatusCode)428);
        (await missing.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("precondition_required");

        var ok = await SendAsync(HttpMethod.Put, $"{Path}/{id}", session.AccessToken, ValidBody("Synthetic Renamed"), etag);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newETag = ok.Headers.ETag!.Tag;
        newETag.ShouldNotBe(etag);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("brandName").GetString().ShouldBe("Synthetic Renamed");

        var stale = await SendAsync(HttpMethod.Put, $"{Path}/{id}", session.AccessToken, ValidBody("Synthetic Lost Update"), etag);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await stale.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("version_mismatch");

        var current = await SendAsync(HttpMethod.Get, $"{Path}/{id}", session.AccessToken);
        (await current.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("brandName").GetString().ShouldBe("Synthetic Renamed");
    }

    [Fact]
    public async Task List_is_paged_and_scoped_to_the_caller()
    {
        var session = await _client.RegisterAsync();
        var other = await _client.RegisterAsync();
        await CreateAsync(other.AccessToken, "Synthetic Not Mine");
        for (var i = 1; i <= 3; i++)
            await CreateAsync(session.AccessToken, $"Synthetic Brand {i}");

        var response = await SendAsync(HttpMethod.Get, $"{Path}?page=1&pageSize=2", session.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page.GetProperty("pageSize").GetInt32().ShouldBe(2);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items[0].GetProperty("brandName").GetString().ShouldBe("Synthetic Brand 3"); // newest first
        items[0].GetProperty("planCount").GetInt32().ShouldBe(0);
        items[0].GetProperty("latestPlanStatus").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    public async Task List_rejects_out_of_range_paging(string query)
    {
        var session = await _client.RegisterAsync();

        var response = await SendAsync(HttpMethod.Get, $"{Path}?{query}", session.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).GetProperty("code").GetString().ShouldBe("validation_failed");
    }
}
