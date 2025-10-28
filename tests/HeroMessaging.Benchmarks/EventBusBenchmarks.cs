using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks for EventBus to validate performance claims:
/// - Target: <1ms p99 latency for event publishing
/// - Target: >100K events/second throughput
/// - Target: <1KB allocation per event in steady state
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory("MessageProcessing")]
public class EventBusBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private EventBus _eventBus = null!;
    private TestEvent _testEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        _serviceProvider = services.BuildServiceProvider();
        var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
        _eventBus = new EventBus(_serviceProvider, timeProvider);
        _testEvent = new TestEvent { Id = 1, Name = "TestEvent" };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Measures single event publishing latency (should be <1ms)
    /// </summary>
    [Benchmark(Description = "Publish single event")]
    public async Task PublishEvent_SingleMessage()
    {
        await _eventBus.Publish(_testEvent);
    }

    /// <summary>
    /// Measures throughput of sequential event publishing
    /// Target: >100K events/second
    /// </summary>
    [Benchmark(Description = "Publish 100 events sequentially")]
    public async Task PublishEvent_SequentialBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            await _eventBus.Publish(_testEvent);
        }
    }

    /// <summary>
    /// Measures event publishing with multiple handlers
    /// </summary>
    [Benchmark(Description = "Publish event with multiple handlers")]
    public async Task PublishEvent_MultipleHandlers()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler2>();
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler3>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        using var provider = services.BuildServiceProvider();
        var timeProvider = provider.GetRequiredService<TimeProvider>();
        var eventBus = new EventBus(provider, timeProvider);

        await eventBus.Publish(_testEvent);
    }
}

// Test event and handlers for benchmarking
public record TestEvent : IEvent
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public class TestEventHandler : IEventHandler<TestEvent>
{
    public Task Handle(TestEvent @event, CancellationToken cancellationToken)
    {
        // Minimal processing - measuring framework overhead
        return Task.CompletedTask;
    }
}

public class TestEventHandler2 : IEventHandler<TestEvent>
{
    public Task Handle(TestEvent @event, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class TestEventHandler3 : IEventHandler<TestEvent>
{
    public Task Handle(TestEvent @event, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
