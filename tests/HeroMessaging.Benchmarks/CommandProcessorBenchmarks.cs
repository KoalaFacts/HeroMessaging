using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks for CommandProcessor to validate performance claims:
/// - Target: <1ms p99 latency for message processing overhead
/// - Target: >100K messages/second single-threaded capability
/// - Target: <1KB allocation per message in steady state
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory("MessageProcessing")]
public class CommandProcessorBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private CommandProcessor _processor = null!;
    private TestCommand _testCommand = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ICommandHandler<TestCommand>, TestCommandHandler>();
        services.AddSingleton<ICommandHandler<TestCommandWithResponse, int>, TestCommandWithResponseHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _processor = new CommandProcessor(_serviceProvider);
        _testCommand = new TestCommand { Id = 1, Name = "TestCommand" };
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
    /// Measures single command processing latency (should be <1ms)
    /// </summary>
    [Benchmark(Description = "Process single command")]
    public async Task ProcessCommand_SingleMessage()
    {
        await _processor.SendAsync(_testCommand);
    }

    /// <summary>
    /// Measures throughput of sequential command processing
    /// Target: >100K messages/second = <10 microseconds per message
    /// </summary>
    [Benchmark(Description = "Process 100 commands sequentially")]
    public async Task ProcessCommand_SequentialBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            await _processor.SendAsync(_testCommand);
        }
    }

    /// <summary>
    /// Measures command with response (generic version)
    /// </summary>
    [Benchmark(Description = "Process command with response")]
    public async Task ProcessCommand_WithResponse()
    {
        var command = new TestCommandWithResponse { Id = 1, Name = "Test" };
        await _processor.SendAsync<int>(command);
    }
}

// Test command and handler for benchmarking
public record TestCommand : ICommand
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public class TestCommandHandler : ICommandHandler<TestCommand>
{
    public Task Handle(TestCommand command, CancellationToken cancellationToken)
    {
        // Minimal processing - we're measuring framework overhead
        return Task.CompletedTask;
    }
}

public record TestCommandWithResponse : ICommand<int>
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public class TestCommandWithResponseHandler : ICommandHandler<TestCommandWithResponse, int>
{
    public Task<int> Handle(TestCommandWithResponse command, CancellationToken cancellationToken)
    {
        return Task.FromResult(command.Id * 2);
    }
}
