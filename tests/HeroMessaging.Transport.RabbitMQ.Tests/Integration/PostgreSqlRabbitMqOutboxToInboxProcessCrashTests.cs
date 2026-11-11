using HeroMessaging.Abstractions.Transport;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// The whole first route across three OS processes: a PostgreSQL Outbox publisher killed after the broker confirmed its
/// first send (so a second publisher delivers that message again, at-least-once), and a PostgreSQL Inbox consumer process
/// on the same RabbitMQ queue. The business ledger is the table of rows the consumer's handler writes, one per handled
/// message: the Inbox must absorb the Outbox duplicate so every message has exactly one row.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlRabbitMqOutboxToInboxProcessCrashTests : RabbitMqIntegrationTestBase
{
    private const int MessageCount = 3;
    private static readonly TimeSpan TakeoverTimeout = TimeSpan.FromSeconds(150);

    [Fact]
    public async Task PublisherKilledAfterConfirm_InboxConsumerProcessAbsorbsTheDuplicate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        PostgreSqlContainer? postgres = null;
        var connectionString = Environment.GetEnvironmentVariable("PostgreSql__ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            postgres = new PostgreSqlBuilder("postgres:17-alpine").WithPassword("postgres").Build();
            await postgres.StartAsync(cancellationToken);
            connectionString = postgres.GetConnectionString();
        }

        var schema = $"route_crash_{Guid.NewGuid():N}";
        var queue = CreateQueueName();
        try
        {
            await ExecuteAsync(connectionString,
                $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.business_ledger (message_id TEXT NOT NULL, process_id INTEGER NOT NULL)",
                cancellationToken);
            var topology = new TransportTopology();
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            await Transport!.ConfigureTopologyAsync(topology, cancellationToken);

            using var consumer = Start(connectionString, schema, queue, "--role", "inbox-consumer");
            Assert.Equal("READY", await consumer.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken));
            var consumerOutput = consumer.ReadToEndAsync(cancellationToken);

            string crashedId;
            var enqueued = new List<string>();
            using (var first = Start(connectionString, schema, queue, "--role", "outbox-publisher",
                "--enqueue", MessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "--crash-point", "after-confirm"))
            {
                string line;
                while (!(line = await first.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken)).StartsWith("CRASHPOINT ", StringComparison.Ordinal))
                {
                    if (line.StartsWith("ENQUEUED ", StringComparison.Ordinal))
                        enqueued.Add(line["ENQUEUED ".Length..]);
                }

                Assert.Equal("CRASHPOINT after-confirm", line[..line.LastIndexOf(' ')]);
                crashedId = line[(line.LastIndexOf(' ') + 1)..];
                // The confirmed message reaches the consumer process before the publisher dies.
                await WaitForAsync(async token => (await ReadLedgerAsync(connectionString, schema, token)).Any(row => row.MessageId == crashedId),
                    TimeSpan.FromSeconds(30), () => "The consumer process did not handle the confirmed message.", cancellationToken);
                first.Kill();
            }

            Assert.Equal(MessageCount, enqueued.Count);
            Assert.Equal(enqueued[0], crashedId);
            Assert.Equal("Processing", (await ReadStatusesAsync(connectionString, schema, "outbox_messages",
                "COALESCE(payload->>'MessageId', payload->>'messageId')", cancellationToken))[crashedId]);

            using var second = Start(connectionString, schema, queue, "--role", "outbox-publisher", "--enqueue", "0");
            Assert.Equal("READY", await second.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken));
            var outbox = new Dictionary<string, string>();
            var inbox = new Dictionary<string, string>();
            await WaitForAsync(async token =>
            {
                outbox = await ReadStatusesAsync(connectionString, schema, "outbox_messages",
                    "COALESCE(payload->>'MessageId', payload->>'messageId')", token);
                inbox = await ReadStatusesAsync(connectionString, schema, "inbox_messages", "id", token);
                return outbox.Count == MessageCount && outbox.Values.All(status => status == "Processed")
                    && enqueued.All(id => inbox.GetValueOrDefault(id) == "Processed");
            }, TakeoverTimeout, () => $"The route did not complete within {TakeoverTimeout}. Outbox: "
                + string.Join(", ", outbox.Select(pair => $"{pair.Key}={pair.Value}"))
                + "; inbox: " + string.Join(", ", inbox.Select(pair => $"{pair.Key}={pair.Value}")), cancellationToken);

            // Give the redelivered duplicate time to reach the consumer process before it stops.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            Assert.Equal(0, await second.StopAsync(TimeSpan.FromSeconds(30), cancellationToken));
            var consumerProcessId = consumer.Id;
            Assert.Equal(0, await consumer.StopAsync(TimeSpan.FromSeconds(30), cancellationToken));

            // Exactly one business effect per message, all in the consumer process.
            var ledger = await ReadLedgerAsync(connectionString, schema, cancellationToken);
            Assert.Equal(enqueued.Order(StringComparer.Ordinal), ledger.Select(row => row.MessageId).Order(StringComparer.Ordinal));
            Assert.All(ledger, row => Assert.Equal(consumerProcessId, row.ProcessId));
            // The publisher side is at-least-once: the consumer process received the killed message a second time, and its
            // Inbox rejected that delivery as a duplicate; nothing else was a duplicate.
            var duplicates = (await consumerOutput).Where(line => line.StartsWith("DUPLICATE ", StringComparison.Ordinal)).ToList();
            Assert.Equal($"DUPLICATE {crashedId}", Assert.Single(duplicates));
            Assert.Equal(MessageCount, (await ReadStatusesAsync(connectionString, schema, "inbox_messages", "id", cancellationToken)).Count);
        }
        finally
        {
            await ExecuteAsync(connectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE", CancellationToken.None);
            if (postgres is not null)
            {
                await postgres.StopAsync(CancellationToken.None);
                await postgres.DisposeAsync();
            }
        }
    }

    private ReliabilityHarnessProcess Start(string connectionString, string schema, string queue, params string[] roleArguments)
    {
        var arguments = new List<string>
        {
            "--pg", connectionString, "--schema", schema, "--rabbit-host", Options!.Host,
            "--rabbit-port", Options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--queue", queue
        };
        arguments.AddRange(roleArguments);
        return ReliabilityHarnessProcess.Start(arguments);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadStatusesAsync(string connectionString, string schema, string table,
        string idExpression, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var exists = new NpgsqlCommand($"SELECT to_regclass('{schema}.{table}') IS NOT NULL", connection))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
                return entries;
        }

        await using var command = new NpgsqlCommand($"SELECT {idExpression}, status FROM {schema}.{table}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries[reader.GetString(0)] = reader.GetString(1);
        return entries;
    }

    private static async Task<List<(string MessageId, int ProcessId)>> ReadLedgerAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT message_id, process_id FROM {schema}.business_ledger", connection);
        var rows = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        return rows;
    }

    private static async Task WaitForAsync(Func<CancellationToken, Task<bool>> condition, TimeSpan limit,
        Func<string> describeTimeout, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            while (!await condition(timeout.Token))
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert.Fail(describeTimeout());
        }
    }
}
