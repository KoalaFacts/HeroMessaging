using System.Diagnostics;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.Serialization.Json;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HeroMessaging.Benchmarks;

internal static class PostgreSqlRabbitMqPipelineBenchmark
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && (!int.TryParse(args[0], out var parsed) || parsed < 1))
            throw new ArgumentException("Usage: --pipeline [message-count]");

        var count = args.Length == 0 ? 100 : int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
        var connectionString = Environment.GetEnvironmentVariable("PostgreSql__ConnectionString")
            ?? throw new InvalidOperationException("Set PostgreSql__ConnectionString to a disposable loopback PostgreSQL instance.");
        var databaseHost = new NpgsqlConnectionStringBuilder(connectionString).Host;
        var rabbitHost = Environment.GetEnvironmentVariable("RabbitMq__Host")
            ?? throw new InvalidOperationException("Set RabbitMq__Host to a disposable loopback RabbitMQ instance.");
        if (!IsLoopback(databaseHost) || !IsLoopback(rabbitHost))
            throw new InvalidOperationException("This benchmark only runs against disposable loopback services.");

        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = connectionString,
            Schema = $"benchmark_pipeline_{Guid.NewGuid():N}"
        };
        var queue = $"benchmark-pipeline-{Guid.NewGuid():N}";
        var serializer = new JsonMessageSerializer();
        await using var transport = new RabbitMqTransport(new RabbitMqTransportOptions
        {
            Host = rabbitHost,
            Port = int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672", System.Globalization.CultureInfo.InvariantCulture),
            UserName = "guest",
            Password = "guest",
            PrefetchCount = 10,
            UsePublisherConfirms = true
        }, NullLoggerFactory.Instance, TimeProvider.System);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageTransport>(transport);
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<PipelineSink>();
        services.AddTransient<IEventHandler<PipelineEvent>, PipelineHandler>();
        services.AddHeroMessaging(builder => builder.WithMediator().WithEventBus().WithOutbox().WithInbox()
            .UsePostgreSqlOutbox(options).UsePostgreSqlInbox(options));
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();
        var startedServices = 0;

        try
        {
            await transport.ConnectAsync();
            var topology = new TransportTopology();
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = false, AutoDelete = true });
            await transport.ConfigureTopologyAsync(topology);
            foreach (var service in hosted)
            {
                await service.StartAsync(CancellationToken.None);
                startedServices++;
            }

            await using var consumer = await InboxTransportSubscription.SubscribeAsync<PipelineEvent>(
                transport, TransportAddress.Queue(queue), provider.GetRequiredService<IInboxProcessor>(), serializer);
            var outbox = provider.GetRequiredService<IOutboxProcessor>();
            var sink = provider.GetRequiredService<PipelineSink>();
            await RunBatchAsync(10, queue, outbox, sink);
            await WaitForDurableCompletionAsync(options, 10);
            var result = await RunBatchAsync(count, queue, outbox, sink);
            var durableSeconds = await WaitForDurableCompletionAsync(options, count + 10, result.Started);
            sink.Validate();

            Console.WriteLine($"Messages: {count}; publisher: {result.PublishSeconds:F2}s; handler throughput: {count / result.HandlerSeconds:F2}/s; durable throughput: {count / durableSeconds:F2}/s");
            Console.WriteLine($"Publish-to-handler latency: p50={Percentile(result.Latencies, 0.50):F1}ms, p95={Percentile(result.Latencies, 0.95):F1}ms, p99={Percentile(result.Latencies, 0.99):F1}ms, max={result.Latencies[^1]:F1}ms");

        }
        finally
        {
            try
            {
                for (var index = startedServices - 1; index >= 0; index--)
                    await hosted[index].StopAsync(CancellationToken.None);
            }
            finally
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static bool IsLoopback(string? host) => host is "localhost" or "127.0.0.1" or "::1";

    private static async Task<BatchResult> RunBatchAsync(int count, string queue, IOutboxProcessor outbox, PipelineSink sink)
    {
        sink.Start(count);
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < count; index++)
        {
            sink.RecordPublished(index);
            await outbox.PublishToOutboxAsync(new PipelineEvent { Sequence = index, RunId = sink.RunId },
                new OutboxOptions { Destination = queue });
        }

        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(3));
        sink.Validate();
        var handlerSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var latencies = sink.GetLatencies();
        Array.Sort(latencies);
        return new BatchResult(started, publishSeconds, handlerSeconds, latencies);
    }

    private static async Task<double> WaitForDurableCompletionAsync(PostgreSqlStorageOptions options, int count, long? started = null)
    {
        var beginning = started ?? Stopwatch.GetTimestamp();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        while (true)
        {
            await using var command = new NpgsqlCommand($"""
                SELECT (SELECT COUNT(*) FROM {options.Schema}.{options.OutboxTableName}),
                       (SELECT COUNT(*) FROM {options.Schema}.{options.OutboxTableName} WHERE status = 'Processed'),
                       (SELECT COUNT(*) FROM {options.Schema}.{options.InboxTableName}),
                       (SELECT COUNT(*) FROM {options.Schema}.{options.InboxTableName} WHERE status = 'Processed')
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(timeout.Token);
            await reader.ReadAsync(timeout.Token);
            if (reader.GetInt64(0) == count && reader.GetInt64(1) == count
                && reader.GetInt64(2) == count && reader.GetInt64(3) == count)
                return Stopwatch.GetElapsedTime(beginning).TotalSeconds;
            await Task.Delay(50, timeout.Token);
        }
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private sealed record BatchResult(long Started, double PublishSeconds, double HandlerSeconds, double[] Latencies);

    public sealed class PipelineEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int Sequence { get; set; }
        public Guid RunId { get; set; }
    }

    public sealed class PipelineHandler(PipelineSink sink) : IEventHandler<PipelineEvent>
    {
        public Task HandleAsync(PipelineEvent message, CancellationToken cancellationToken = default)
        {
            sink.RecordHandled(message.RunId, message.Sequence);
            return Task.CompletedTask;
        }
    }

    public sealed class PipelineSink
    {
        private long[] _published = [];
        private long[] _handled = [];
        private int _completed;
        private int _duplicates;
        private TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;
        public Guid RunId { get; private set; }

        public void Start(int count)
        {
            _published = new long[count];
            _handled = new long[count];
            _completed = 0;
            _duplicates = 0;
            RunId = Guid.NewGuid();
            _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void RecordPublished(int sequence) => _published[sequence] = Stopwatch.GetTimestamp();

        public void RecordHandled(Guid runId, int sequence)
        {
            if (runId != RunId || sequence < 0 || sequence >= _handled.Length)
            {
                Interlocked.Increment(ref _duplicates);
                return;
            }

            if (Interlocked.CompareExchange(ref _handled[sequence], Stopwatch.GetTimestamp(), 0) != 0)
            {
                Interlocked.Increment(ref _duplicates);
                return;
            }

            if (Interlocked.Increment(ref _completed) == _handled.Length)
                _completion.TrySetResult();
        }

        public void Validate()
        {
            if (Volatile.Read(ref _duplicates) != 0)
                throw new InvalidOperationException("The handler ran more than once for a benchmark event.");
        }

        public double[] GetLatencies()
        {
            var latencies = new double[_published.Length];
            for (var index = 0; index < latencies.Length; index++)
                latencies[index] = Stopwatch.GetElapsedTime(_published[index], _handled[index]).TotalMilliseconds;
            return latencies;
        }
    }
}
