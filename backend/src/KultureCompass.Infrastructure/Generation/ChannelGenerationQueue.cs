using System.Threading.Channels;
using KultureCompass.Application.Abstractions;

namespace KultureCompass.Infrastructure.Generation;

/// <summary>Bounded in-process queue (BE-01 §4, capacity 100). When full, enqueue waits rather than dropping — the
/// worker drains at MaxConcurrency, and a plan id lost to a crash is re-enqueued by <see cref="StuckPlanRecovery"/>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "It is a queue; name fixed by BE-01 §4.")]
public sealed class ChannelGenerationQueue : IGenerationQueue
{
    public const int Capacity = 100;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid planId, CancellationToken ct) => _channel.Writer.WriteAsync(planId, ct);
}
