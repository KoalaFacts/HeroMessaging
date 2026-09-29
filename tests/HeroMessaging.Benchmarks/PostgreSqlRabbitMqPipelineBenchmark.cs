using System.Diagnostics;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
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
    public static async Task RunAsync(string[] args, bool direct = false)
    {
        if (args.Length > (direct ? 1 : 2))
            throw new ArgumentException("Usage: --pipeline [message-count] [external-concurrency] or --pipeline-direct [message-count]");
        if (args.Length >= 1 && (!int.TryParse(args[0], out var parsedCount) || parsedCount < 1))
            throw new ArgumentException("Usage: --pipeline [message-count] [external-concurrency] or --pipeline-direct [message-count]");
        if (args.Length == 2 && (!int.TryParse(args[1], out var parsedConcurrency) || parsedConcurrency < 1))
            throw new ArgumentException("Usage: --pipeline [message-count] [external-concurrency] or --pipeline-direct [message-count]");

        var count = args.Length == 0 ? 100 : int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
        var externalConcurrency = args.Length < 2 ? 4 : int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
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
        services.AddHeroMessaging(builder =>
        {
            builder.WithMediator().WithEventBus().WithInbox().UsePostgreSqlInbox(options);
            if (!direct)
            {
                builder.ConfigureProcessing(processing => processing.ExternalOutboxMaxConcurrency = externalConcurrency);
                builder.WithOutbox().UsePostgreSqlOutbox(options);
            }
        });
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
            var outbox = direct ? null : provider.GetRequiredService<IOutboxProcessor>();
            var sink = provider.GetRequiredService<PipelineSink>();
            var outboxOptions = new OutboxOptions { Destination = queue };
            async Task PublishAsync(PipelineEvent message)
            {
                if (!direct)
                {
                    await outbox!.PublishToOutboxAsync(message, outboxOptions);
                    return;
                }

                await transport.SendConfirmedAsync(TransportAddress.Queue(queue), new TransportEnvelope
                {
                    MessageId = message.MessageId.ToString(),
                    MessageType = typeof(PipelineEvent).AssemblyQualifiedName!,
                    ContentType = serializer.ContentType,
                    Body = await serializer.SerializeAsync(message)
                });
            }

            await RunBatchAsync(10, PublishAsync, sink);
            await WaitForDurableCompletionAsync(options, 10, direct);
            var result = await RunBatchAsync(count, PublishAsync, sink);
            var durableSeconds = await WaitForDurableCompletionAsync(options, count + 10, direct, result.Started);
            sink.Validate();

            Console.WriteLine($"Path: {(direct ? "RabbitMQ -> Inbox" : "PostgreSQL -> RabbitMQ -> Inbox")}; messages: {count}; external concurrency: {(direct ? "n/a" : externalConcurrency)}; publisher: {result.PublishSeconds:F2}s; handler throughput: {count / result.HandlerSeconds:F2}/s; durable throughput: {count / durableSeconds:F2}/s");
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

    private static async Task<BatchResult> RunBatchAsync(int count, Func<PipelineEvent, Task> publish, PipelineSink sink)
    {
        sink.Start(count);
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < count; index++)
        {
            sink.RecordPublished(index);
            await publish(new PipelineEvent { Sequence = index, RunId = sink.RunId });
        }

        var publishSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await sink.Completion.WaitAsync(TimeSpan.FromMinutes(3));
        sink.Validate();
        var handlerSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var latencies = sink.GetLatencies();
        Array.Sort(latencies);
        return new BatchResult(started, publishSeconds, handlerSeconds, latencies);
    }

    private static async Task<double> WaitForDurableCompletionAsync(PostgreSqlStorageOptions options, int count, bool direct, long? started = null)
    {
        var beginning = started ?? Stopwatch.GetTimestamp();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        while (true)
        {
            var outboxCounts = direct ? "0::bigint, 0::bigint" : $"""
                (SELECT COUNT(*) FROM {options.Schema}.{options.OutboxTableName}),
                (SELECT COUNT(*) FROM {options.Schema}.{options.OutboxTableName} WHERE status = 'Processed')
                """;
            await using var command = new NpgsqlCommand($"""
                SELECT {outboxCounts},
                       (SELECT COUNT(*) FROM {options.Schema}.{options.InboxTableName}),
                       (SELECT COUNT(*) FROM {options.Schema}.{options.InboxTableName} WHERE status = 'Processed')
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(timeout.Token);
            await reader.ReadAsync(timeout.Token);
            if (reader.GetInt64(0) == (direct ? 0 : count) && reader.GetInt64(1) == (direct ? 0 : count)
                && reader.GetInt64(2) == count && reader.GetInt64(3) == count)
                return Stopwatch.GetElapsedTime(beginning).TotalSeconds;
            await Task.Delay(50, timeout.Token);
        }
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private sealed record BatchResult(long Started, double PublishSeconds, double HandlerSeconds, double[] Latencies);
}
