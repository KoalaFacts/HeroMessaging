using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks for QueryProcessor to validate performance claims:
/// - Target: <1ms p99 latency for query processing
/// - Target: >100K queries/second throughput
/// - Target: <1KB allocation per query in steady state
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory("MessageProcessing")]
public class QueryProcessorBenchmarks
{
    private IServiceProvider _serviceProvider = null!;
    private QueryProcessor _processor = null!;
    private TestQuery _testQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IQueryHandler<TestQuery, string>, TestQueryHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _processor = new QueryProcessor(_serviceProvider);
        _testQuery = new TestQuery { Id = 1 };
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
    /// Measures single query processing latency (should be <1ms)
    /// </summary>
    [Benchmark(Description = "Process single query")]
    public async Task ProcessQuery_SingleMessage()
    {
        await _processor.SendAsync(_testQuery);
    }

    /// <summary>
    /// Measures throughput of sequential query processing
    /// Target: >100K queries/second
    /// </summary>
    [Benchmark(Description = "Process 100 queries sequentially")]
    public async Task ProcessQuery_SequentialBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            await _processor.SendAsync(_testQuery);
        }
    }
}

// Test query and handler for benchmarking
public record TestQuery : IQuery<string>
{
    public int Id { get; init; }
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public class TestQueryHandler : IQueryHandler<TestQuery, string>
{
    public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
    {
        // Minimal processing - we're measuring framework overhead
        return Task.FromResult($"Result_{query.Id}");
    }
}
