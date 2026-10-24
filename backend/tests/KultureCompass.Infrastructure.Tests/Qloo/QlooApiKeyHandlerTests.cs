using KultureCompass.Infrastructure.Qloo;
using Microsoft.Extensions.Options;
using Shouldly;

namespace KultureCompass.Infrastructure.Tests.Qloo;

public class QlooApiKeyHandlerTests
{
    [Fact]
    public async Task Adds_the_X_Api_Key_header_from_options()
    {
        var fake = new FakeHttpMessageHandler().Enqueue(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var handler = new QlooApiKeyHandler(Options.Create(new QlooOptions { ApiKey = "super-secret-key" }))
        {
            InnerHandler = fake,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await client.GetAsync("/search?query=x");

        fake.Requests.Single().Headers.GetValues("X-Api-Key").Single().ShouldBe("super-secret-key");
    }
}
