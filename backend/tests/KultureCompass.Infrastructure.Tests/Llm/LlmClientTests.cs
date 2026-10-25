using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KultureCompass.Application.Abstractions;
using KultureCompass.Application.Generation;
using KultureCompass.Infrastructure.Configuration;
using KultureCompass.Infrastructure.Llm;
using KultureCompass.Infrastructure.Tests.Qloo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Llm;

public class GeminiLlmClientTests
{
    private const string FakeKey = "synthetic-test-key-not-real";

    private static GeminiLlmClient Client(FakeHttpMessageHandler fake) => new(
        new HttpClient(fake),
        Options.Create(new LlmOptions { Provider = "Gemini", ApiKey = FakeKey, Model = "gemini-test", MaxRetryDelaySeconds = 0 }),
        NullLogger<GeminiLlmClient>.Instance);

    // Unique schema names per test: the client caches "needs simplified schema" per name process-wide.
    private static LlmJsonRequest Request(string schemaName) => new(
        "system", "user", JsonNode.Parse("""{"$schema":"x","type":"object","properties":{"a":{"type":"array","minItems":1,"maxItems":3}}}""")!,
        schemaName, 500, Temperature: 0.9);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string OkBody = """
        {"candidates":[{"content":{"parts":[{"text":"thinking...","thought":true},{"text":"{\"a\":"},{"text":"[1]}"}]},"finishReason":"STOP"}],
         "usageMetadata":{"promptTokenCount":17,"candidatesTokenCount":12,"thoughtsTokenCount":5}}
        """;

    [Fact]
    public async Task Sends_key_in_header_only_and_structured_output_config()
    {
        string? body = null;
        var fake = new FakeHttpMessageHandler().Enqueue(r =>
        {
            body = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, OkBody);
        });

        var result = await Client(fake).GenerateJsonAsync(Request(nameof(Sends_key_in_header_only_and_structured_output_config)), CancellationToken.None);

        var request = fake.Requests.Single();
        request.RequestUri!.AbsolutePath.ShouldBe("/v1beta/models/gemini-test:generateContent");
        request.RequestUri.Query.ShouldBeEmpty();
        request.Headers.GetValues("x-goog-api-key").Single().ShouldBe(FakeKey);
        var json = JsonNode.Parse(body!)!;
        json["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().ShouldBe("system");
        json["contents"]![0]!["role"]!.GetValue<string>().ShouldBe("user");
        var config = json["generationConfig"]!;
        config["responseMimeType"]!.GetValue<string>().ShouldBe("application/json");
        config["responseJsonSchema"]!["$schema"].ShouldBeNull();
        config["responseJsonSchema"]!["properties"]!["a"]!["maxItems"]!.GetValue<int>().ShouldBe(3);
        config["temperature"]!.GetValue<double>().ShouldBe(0.5); // capped
        config["maxOutputTokens"]!.GetValue<int>().ShouldBe(500);

        var success = result.ShouldBeOfType<LlmResult.Success>();
        success.Json.ShouldBe("{\"a\":[1]}"); // thought parts excluded
        success.Usage.ShouldBe(new LlmUsage(17, 12, 5));
    }

    [Fact]
    public async Task A_400_retries_once_with_array_bounds_stripped_and_remembers_it_per_schema()
    {
        var bodies = new List<string>();
        var fake = new FakeHttpMessageHandler()
            .Enqueue(r => { bodies.Add(r.Content!.ReadAsStringAsync().Result); return Json(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT"}}"""); })
            .Enqueue(r => { bodies.Add(r.Content!.ReadAsStringAsync().Result); return Json(HttpStatusCode.OK, OkBody); });
        var client = Client(fake);
        var schemaName = nameof(A_400_retries_once_with_array_bounds_stripped_and_remembers_it_per_schema);

        (await client.GenerateJsonAsync(Request(schemaName), CancellationToken.None)).ShouldBeOfType<LlmResult.Success>();
        await client.GenerateJsonAsync(Request(schemaName), CancellationToken.None);

        fake.CallCount.ShouldBe(3); // 400, simplified 200, then straight to simplified next time
        bodies[0].ShouldContain("maxItems");
        bodies[1].ShouldNotContain("maxItems");
        bodies[1].ShouldNotContain("minItems");
        bodies[2].ShouldNotContain("maxItems");
    }

    [Fact]
    public async Task A_400_on_the_simplified_schema_is_not_retried_again()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(_ => Json(HttpStatusCode.BadRequest, "{}"));

        var result = await Client(fake).GenerateJsonAsync(Request(nameof(A_400_on_the_simplified_schema_is_not_retried_again)), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Unavailable);
        fake.CallCount.ShouldBe(2);
    }

    [Theory]
    [InlineData("MAX_TOKENS", LlmErrorKind.Truncated)]
    [InlineData("SAFETY", LlmErrorKind.Refused)]
    [InlineData("PROHIBITED_CONTENT", LlmErrorKind.Refused)]
    public void Finish_reasons_map_to_error_kinds(string finishReason, LlmErrorKind expected) =>
        GeminiLlmClient.ParseSuccess($$"""{"candidates":[{"content":{"parts":[{"text":"{"}]},"finishReason":"{{finishReason}}"}]}""")
            .ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(expected);

    [Fact]
    public void Blocked_prompt_with_no_candidates_is_Refused() =>
        GeminiLlmClient.ParseSuccess("""{"promptFeedback":{"blockReason":"SAFETY"}}""")
            .ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Refused);

    [Fact]
    public async Task Daily_quota_429_is_QuotaExceeded_and_not_retried()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(Json(HttpStatusCode.TooManyRequests,
            """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}"""));

        var result = await Client(fake).GenerateJsonAsync(Request("quota-day"), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.QuotaExceeded);
        fake.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Per_minute_429_is_retried_once_honouring_retryDelay()
    {
        var fake = new FakeHttpMessageHandler()
            .Enqueue(Json(HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"retryDelay":"3s"}]}}"""))
            .Enqueue(Json(HttpStatusCode.OK, OkBody));

        var result = await Client(fake).GenerateJsonAsync(Request("quota-minute"), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Success>();
        fake.CallCount.ShouldBe(2);
        LlmHttpSender.ParseRetryDelay("""{"retryDelay": "21s"}""").ShouldBe(TimeSpan.FromSeconds(21));
    }

    [Fact]
    public async Task Persistent_5xx_is_retried_at_most_twice_then_Unavailable()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var result = await Client(fake).GenerateJsonAsync(Request("5xx"), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Unavailable);
        fake.CallCount.ShouldBe(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auth_errors_are_not_retried(HttpStatusCode status)
    {
        var fake = new FakeHttpMessageHandler().Enqueue(Json(status, "{}"));

        var result = await Client(fake).GenerateJsonAsync(Request("auth-" + status), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.AuthFailed);
        fake.CallCount.ShouldBe(1);
    }
}

public class OpenAiCompatibleLlmClientTests
{
    private static OpenAiCompatibleLlmClient Client(FakeHttpMessageHandler fake) => new(
        new HttpClient(fake),
        Options.Create(new LlmOptions { Provider = "OpenAiCompatible", ApiKey = "synthetic", Model = "m", BaseUrl = "https://llm.example/v1", MaxRetryDelaySeconds = 0 }),
        NullLogger<OpenAiCompatibleLlmClient>.Instance);

    private static LlmJsonRequest Request(string name) => new("system", "user", JsonNode.Parse("""{"type":"object"}""")!, name, 100);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Uses_strict_json_schema_then_falls_back_to_json_object_on_400()
    {
        var bodies = new List<string>();
        var fake = new FakeHttpMessageHandler()
            .Enqueue(r => { bodies.Add(r.Content!.ReadAsStringAsync().Result); return Json(HttpStatusCode.BadRequest, "{}"); })
            .Enqueue(r =>
            {
                bodies.Add(r.Content!.ReadAsStringAsync().Result);
                return Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1}}""");
            });

        var result = await Client(fake).GenerateJsonAsync(Request("oai-fallback"), CancellationToken.None);

        result.ShouldBeOfType<LlmResult.Success>().Usage.ShouldBe(new LlmUsage(3, 1));
        fake.Requests[0].RequestUri!.ToString().ShouldBe("https://llm.example/v1/chat/completions");
        fake.Requests[0].Headers.Authorization!.Scheme.ShouldBe("Bearer");
        JsonNode.Parse(bodies[0])!["response_format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        JsonNode.Parse(bodies[0])!["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>().ShouldBeTrue();
        JsonNode.Parse(bodies[1])!["response_format"]!["type"]!.GetValue<string>().ShouldBe("json_object");
    }

    [Fact]
    public void Length_finish_is_Truncated_and_refusal_is_Refused()
    {
        OpenAiCompatibleLlmClient.ParseSuccess("""{"choices":[{"message":{"content":"{"},"finish_reason":"length"}]}""")
            .ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Truncated);
        OpenAiCompatibleLlmClient.ParseSuccess("""{"choices":[{"message":{"content":null,"refusal":"no"},"finish_reason":"stop"}]}""")
            .ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Refused);
    }

    [Fact]
    public async Task Insufficient_quota_429_is_QuotaExceeded()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"insufficient_quota"}}"""));

        (await Client(fake).GenerateJsonAsync(Request("oai-quota"), CancellationToken.None))
            .ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.QuotaExceeded);
    }
}

public class MockLlmClientTests
{
    private static MockLlmClient Mock(MockLlmScenario scenario = MockLlmScenario.Valid) =>
        new(Options.Create(new LlmOptions { MockScenario = scenario.ToString() }));

    private const string UserContent = """
        <brief>{"category":"Streetwear","marketingObjective":"Sales"}</brief>
        <evidence>[{"key":"ev_001","name":"Synthetic Afrobeat Star","type":"artist","quality":"Strong"},
                   {"key":"ev_002","name":"Synthetic Lagos Streetwear Co","type":"brand","quality":"Strong"},
                   {"key":"ev_003","name":"Synthetic Street Culture","type":"tag","quality":"Moderate"}]</evidence>
        <limitations>[]</limitations>
        <constraints>{"collaborations":"0-4"}</constraints>
        """;

    private static LlmJsonRequest Synthesis() =>
        new("s", UserContent, PromptLibrary.GrowthPlanSchema(), PromptLibrary.GrowthPlanSchemaName, 1000);

    [Fact]
    public async Task Valid_output_is_built_only_from_prompt_evidence_and_is_labelled_mock()
    {
        var result = (await Mock().GenerateJsonAsync(Synthesis(), CancellationToken.None)).ShouldBeOfType<LlmResult.Success>();

        result.Json.ShouldContain("[MOCK]");
        var keys = System.Text.RegularExpressions.Regex.Matches(result.Json, "ev_\\d{3}").Select(m => m.Value).Distinct();
        keys.ShouldBeSubsetOf(["ev_001", "ev_002", "ev_003"]);
        var draft = System.Text.Json.JsonSerializer.Deserialize<GrowthPlanDraft>(result.Json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        draft.Collaborations!.ShouldAllBe(c => c.PartnerEvidenceKey != "ev_003"); // a tag is never a partner
        draft.Insights!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Interpretation_requests_never_suggest_seed_entities()
    {
        var result = await Mock().GenerateJsonAsync(
            new("s", UserContent, PromptLibrary.InterpretationSchema(), PromptLibrary.InterpretationSchemaName, 500), CancellationToken.None);

        JsonNode.Parse(result.ShouldBeOfType<LlmResult.Success>().Json)!["suggestedSeedNames"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Failure_scenarios_behave_as_named()
    {
        (await Mock(MockLlmScenario.QuotaExceeded).GenerateJsonAsync(Synthesis(), default)).ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.QuotaExceeded);
        (await Mock(MockLlmScenario.Truncated).GenerateJsonAsync(Synthesis(), default)).ShouldBeOfType<LlmResult.Failure>().Kind.ShouldBe(LlmErrorKind.Truncated);
        (await Mock(MockLlmScenario.UnknownEvidenceKey).GenerateJsonAsync(Synthesis(), default)).ShouldBeOfType<LlmResult.Success>().Json.ShouldContain("ev_999");

        var once = Mock(MockLlmScenario.InvalidJsonOnce);
        (await once.GenerateJsonAsync(Synthesis(), default)).ShouldBeOfType<LlmResult.Success>().Json.ShouldEndWith(": ");
        (await once.GenerateJsonAsync(Synthesis(), default)).ShouldBeOfType<LlmResult.Success>().Json.ShouldContain("[MOCK]");
    }
}
