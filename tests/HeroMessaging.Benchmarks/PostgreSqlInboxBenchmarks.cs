using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Npgsql;

namespace HeroMessaging.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 2, iterationCount: 5)]
[BenchmarkCategory("PostgreSql")]
public class PostgreSqlInboxBenchmarks
{
    private const int BatchSize = 100;
    private readonly InboxOptions _inboxOptions = new();
    private PostgreSqlInboxStorage _storage = null!;
    private PostgreSqlStorageOptions _options = null!;
    private TestEvent[] _duplicates = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var connectionString = Environment.GetEnvironmentVariable("PostgreSql__ConnectionString")
            ?? throw new InvalidOperationException("Set PostgreSql__ConnectionString before running PostgreSQL benchmarks.");
        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = connectionString,
            Schema = $"benchmark_inbox_{Guid.NewGuid():N}"
        };
        _storage = new PostgreSqlInboxStorage(_options, TimeProvider.System,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        _duplicates = [.. Enumerable.Range(0, BatchSize).Select(static _ => new TestEvent())];
        foreach (var message in _duplicates)
            await _storage.AddAsync(message, _inboxOptions);
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public async Task InsertNew()
    {
        for (var i = 0; i < BatchSize; i++)
            await _storage.AddAsync(new TestEvent(), _inboxOptions);
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public async Task InsertDuplicate()
    {
        foreach (var message in _duplicates)
            await _storage.AddAsync(message, _inboxOptions);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_options.Schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    public sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
