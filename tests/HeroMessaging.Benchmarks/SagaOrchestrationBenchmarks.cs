using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks for SagaOrchestrator to validate performance claims:
/// - Target: <1ms p99 latency for saga processing
/// - Target: >100K saga events/second throughput
/// - Target: <1KB allocation per saga event in steady state
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory("SagaProcessing")]
public class SagaOrchestrationBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private SagaOrchestrator<TestSaga> _orchestrator = null!;
    private TestSagaStartEvent _startEvent = null!;
    private TestSagaCompleteEvent _completeEvent = null!;
    private InMemorySagaRepository<TestSaga> _repository = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        _serviceProvider = services.BuildServiceProvider();

        var timeProvider = TimeProvider.System;
        _repository = new InMemorySagaRepository<TestSaga>(timeProvider);

        var stateMachine = new StateMachineBuilder<TestSaga>()
            .WithState("Initial")
            .WithState("Started")
            .WithState("Completed")
            .WithInitialState("Initial")
            .AddTransition("Initial", "Started", new TestSagaStartEvent(), (context) =>
            {
                return Task.CompletedTask;
            })
            .AddTransition("Started", "Completed", new TestSagaCompleteEvent(), (context) =>
            {
                return Task.CompletedTask;
            })
            .Build();

        _orchestrator = new SagaOrchestrator<TestSaga>(
            _repository,
            stateMachine,
            _serviceProvider,
            NullLogger<SagaOrchestrator<TestSaga>>.Instance,
            timeProvider);

        var correlationId = Guid.NewGuid();
        _startEvent = new TestSagaStartEvent { CorrelationId = correlationId.ToString() };
        _completeEvent = new TestSagaCompleteEvent { CorrelationId = correlationId.ToString() };
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
    /// Measures saga creation and first event processing (should be <1ms)
    /// </summary>
    [Benchmark(Description = "Process saga start event")]
    public async Task ProcessSaga_StartEvent()
    {
        var correlationId = Guid.NewGuid();
        var startEvent = new TestSagaStartEvent { CorrelationId = correlationId.ToString() };
        await _orchestrator.ProcessAsync(startEvent);
    }

    /// <summary>
    /// Measures full saga lifecycle (create, transition, complete)
    /// </summary>
    [Benchmark(Description = "Process full saga lifecycle")]
    public async Task ProcessSaga_FullLifecycle()
    {
        var correlationId = Guid.NewGuid();
        var startEvent = new TestSagaStartEvent { CorrelationId = correlationId.ToString() };
        var completeEvent = new TestSagaCompleteEvent { CorrelationId = correlationId.ToString() };

        await _orchestrator.ProcessAsync(startEvent);
        await _orchestrator.ProcessAsync(completeEvent);
    }

    /// <summary>
    /// Measures throughput of sequential saga processing
    /// Target: >100K saga events/second
    /// </summary>
    [Benchmark(Description = "Process 100 saga events sequentially")]
    public async Task ProcessSaga_SequentialBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            var correlationId = Guid.NewGuid();
            var startEvent = new TestSagaStartEvent { CorrelationId = correlationId.ToString() };
            await _orchestrator.ProcessAsync(startEvent);
        }
    }
}

// Test saga and events for benchmarking
public class TestSaga : ISaga
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = "Initial";
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TestSagaStartEvent : IEvent, IMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class TestSagaCompleteEvent : IEvent, IMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
