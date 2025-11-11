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
    private IServiceProvider _multiHandlerServiceProvider = null!;
    private EventBus _eventBus = null!;
    private EventBus _multiHandlerEventBus = null!;
    private TestEvent _testEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup for single handler benchmarks
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        _serviceProvider = services.BuildServiceProvider();
        var logger = _serviceProvider.GetRequiredService<ILogger<EventBus>>();
        _eventBus = new EventBus(_serviceProvider, logger);

        // Setup for multiple handler benchmark
        var multiServices = new ServiceCollection();
        multiServices.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        multiServices.AddSingleton<IEventHandler<TestEvent>, TestEventHandler>();
        multiServices.AddSingleton<IEventHandler<TestEvent>, TestEventHandler2>();
        multiServices.AddSingleton<IEventHandler<TestEvent>, TestEventHandler3>();
        multiServices.AddSingleton<TimeProvider>(TimeProvider.System);

        _multiHandlerServiceProvider = multiServices.BuildServiceProvider();
        var multiLogger = _multiHandlerServiceProvider.GetRequiredService<ILogger<EventBus>>();
        _multiHandlerEventBus = new EventBus(_multiHandlerServiceProvider, multiLogger);

        _testEvent = new TestEvent { Id = 1, Name = "TestEvent" };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        if (_multiHandlerServiceProvider is IDisposable multiDisposable)
        {
            multiDisposable.Dispose();
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
        await _multiHandlerEventBus.Publish(_testEvent);
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
