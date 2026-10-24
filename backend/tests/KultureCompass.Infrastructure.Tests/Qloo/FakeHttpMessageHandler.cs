namespace KultureCompass.Infrastructure.Tests.Qloo;

/// <summary>Hand-rolled fake primary HttpMessageHandler. Each call pops a response factory off a queue; a single
/// factory is reused for every remaining call when the queue has one item left, which makes it convenient to
/// simulate "N transient failures, then nothing left" without over-provisioning responses.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount { get; private set; }

    public FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    public FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory) =>
        Enqueue((request, _) => Task.FromResult(factory(request)));

    public FakeHttpMessageHandler Enqueue(HttpResponseMessage response) => Enqueue(_ => response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);

        var factory = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        return factory(request, cancellationToken);
    }
}
