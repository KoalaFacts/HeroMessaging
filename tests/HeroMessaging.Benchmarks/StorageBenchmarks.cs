using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Storage;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Benchmarks for Storage operations to validate performance claims:
/// - Target: <1ms p99 latency for storage operations
/// - Target: >100K operations/second throughput
/// - Target: <1KB allocation per operation in steady state
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory("Storage")]
public class StorageBenchmarks
{
    private InMemoryMessageStorage _messageStorage = null!;
    private TestMessage _testMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        var timeProvider = TimeProvider.System;
        _messageStorage = new InMemoryMessageStorage(timeProvider);
        _testMessage = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Measures message storage latency (should be <1ms)
    /// </summary>
    [Benchmark(Description = "Store single message")]
    public async Task Storage_StoreMessage()
    {
        await _messageStorage.StoreAsync(_testMessage);
    }

    /// <summary>
    /// Measures message retrieval latency
    /// </summary>
    [Benchmark(Description = "Retrieve message by ID")]
    public async Task Storage_RetrieveMessage()
    {
        var id = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = id,
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
        await _messageStorage.StoreAsync(message);
        await _messageStorage.RetrieveAsync(id);
    }

    /// <summary>
    /// Measures throughput of sequential storage operations
    /// Target: >100K operations/second
    /// </summary>
    [Benchmark(Description = "Store 100 messages sequentially")]
    public async Task Storage_SequentialBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            var message = new TestMessage
            {
                MessageId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow
            };
            await _messageStorage.StoreAsync(message);
        }
    }
}

// Test message for benchmarking
public class TestMessage : IMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public string? CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? CausationId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
