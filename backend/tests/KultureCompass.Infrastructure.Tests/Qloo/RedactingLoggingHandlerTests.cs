using KultureCompass.Infrastructure.Qloo;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

public class RedactingLoggingHandlerTests
{
    [Fact]
    public async Task Logs_method_path_status_and_attempt_but_never_the_api_key_or_query_string()
    {
        var logger = new CapturingLogger<RedactingLoggingHandler>();
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var apiKeyHandler = new QlooApiKeyHandler(
            Microsoft.Extensions.Options.Options.Create(new QlooOptions { ApiKey = "top-secret-key-should-not-leak" }))
        {
            InnerHandler = fake,
        };
        var handler = new RedactingLoggingHandler(logger) { InnerHandler = apiKeyHandler };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await client.GetAsync("/search?query=Burna+Boy&secret=should-not-appear");

        logger.Messages.ShouldNotBeEmpty();
        var joined = string.Join('\n', logger.Messages);
        joined.ShouldContain("/search");
        joined.ShouldContain("200");
        joined.ShouldContain("attempt");
        joined.ShouldNotContain("top-secret-key-should-not-leak");
        joined.ShouldNotContain("query=");
        joined.ShouldNotContain("secret=should-not-appear");
    }
}
