using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.AotSmoke;

public sealed record ProbeEvent : IEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public int Value { get; init; }
}

public sealed class ProbeState
{
    public string? StoredId { get; set; }
    public int Calls { get; set; }
    public int Disposals { get; set; }
}

public sealed class ProbeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

public sealed class ProbeHandler(IBasicMessageStorage storage, ProbeState state) : IEventHandler<ProbeEvent>, IAsyncDisposable
{
    public async Task HandleAsync(ProbeEvent message, CancellationToken cancellationToken = default)
    {
        state.StoredId = await storage.StoreAsync(message, new MessageStorageOptions { Collection = "aot-probe" }, cancellationToken);
        state.Calls++;
    }

    public ValueTask DisposeAsync()
    {
        state.Disposals++;
        return ValueTask.CompletedTask;
    }
}
