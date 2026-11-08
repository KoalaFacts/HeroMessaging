using HeroMessaging.Abstractions.Transport;
using HeroMessaging.ReliabilityHarness;
using HeroMessaging.Serialization.Json;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Two separate OS processes consume one RabbitMQ queue into a PostgreSQL Inbox. The first is killed at a
/// declared crash point while holding an unacknowledged delivery; the second receives the redelivery. The
/// business ledger is the table of rows the Inbox handler writes, one per handled message.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlRabbitMqInboxProcessCrashTests : RabbitMqIntegrationTestBase
{
    private const int MessageCount = 3;
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData("before-inbox", false)]
    [InlineData("after-inbox", true)]
    public async Task ConsumerKilledBeforeAcknowledgement_SecondProcessHandlesEveryMessageOnce(string crashPoint, bool entryPersistedBeforeKill)
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

        var schema = $"inbox_crash_{Guid.NewGuid():N}";
        var queue = CreateQueueName();
        try
        {
            await ExecuteAsync(connectionString,
                $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.business_ledger (message_id TEXT NOT NULL, process_id INTEGER NOT NULL)",
                cancellationToken);
            var topology = new TransportTopology();
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            await Transport!.ConfigureTopologyAsync(topology, cancellationToken);

            var serializer = new JsonMessageSerializer();
            var sent = new List<string>();
            for (var index = 0; index < MessageCount; index++)
            {
                var message = new HarnessOrderPlaced { OrderNumber = index };
                await Transport.SendConfirmedAsync(TransportAddress.Queue(queue), new TransportEnvelope
                {
                    MessageId = message.MessageId.ToString(),
                    MessageType = typeof(HarnessOrderPlaced).AssemblyQualifiedName!,
                    ContentType = serializer.ContentType,
                    Body = await serializer.SerializeAsync(message, cancellationToken)
                }, cancellationToken);
                sent.Add(message.MessageId.ToString());
            }

            string crashedId;
            using (var first = StartConsumer(connectionString, schema, queue, crashPoint))
            {
                Assert.Equal("READY", await first.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken));
                var line = await first.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken);
                Assert.Equal($"CRASHPOINT {crashPoint}", line[..line.LastIndexOf(' ')]);
                crashedId = line[(line.LastIndexOf(' ') + 1)..];
                first.Kill();
            }

            Assert.Equal(sent[0], crashedId);
            var inboxAfterKill = await ReadInboxAsync(connectionString, schema, cancellationToken);
            if (entryPersistedBeforeKill)
                Assert.Equal("Pending", inboxAfterKill[crashedId]);
            else
                Assert.False(inboxAfterKill.ContainsKey(crashedId), "No Inbox entry may exist before the admission.");
            Assert.Empty(await ReadLedgerAsync(connectionString, schema, cancellationToken));

            using var second = StartConsumer(connectionString, schema, queue, crashPoint: null);
            Assert.Equal("READY", await second.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken));
            var inbox = new Dictionary<string, string>();
            var ledger = new List<(string MessageId, int ProcessId)>();
            await WaitForAsync(async token =>
            {
                inbox = await ReadInboxAsync(connectionString, schema, token);
                ledger = await ReadLedgerAsync(connectionString, schema, token);
                return sent.All(id => inbox.GetValueOrDefault(id) == "Processed")
                    && sent.All(id => ledger.Any(row => row.MessageId == id));
            }, RecoveryTimeout, () => $"Second process did not handle every message within {RecoveryTimeout}. Inbox: "
                + string.Join(", ", inbox.Select(pair => $"{pair.Key}={pair.Value}"))
                + "; ledger: " + string.Join(", ", ledger.Select(row => $"{row.MessageId}@{row.ProcessId}")), cancellationToken);

            // Give a stray duplicate time to be handled before the ledger is checked.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var secondProcessId = second.Id;
            Assert.Equal(0, await second.StopAsync(TimeSpan.FromSeconds(30), cancellationToken));

            ledger = await ReadLedgerAsync(connectionString, schema, cancellationToken);
            Assert.Equal(sent.Order(StringComparer.Ordinal), ledger.Select(row => row.MessageId).Order(StringComparer.Ordinal));
            Assert.Equal(secondProcessId, Assert.Single(ledger, row => row.MessageId == crashedId).ProcessId);
            Assert.Equal(MessageCount, (await ReadInboxAsync(connectionString, schema, cancellationToken)).Count);
            Assert.Equal(0u, await GetReadyCountAsync(queue, cancellationToken));
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

    private ReliabilityHarnessProcess StartConsumer(string connectionString, string schema, string queue, string? crashPoint)
    {
        var arguments = new List<string>
        {
            "--role", "inbox-consumer", "--pg", connectionString, "--schema", schema, "--rabbit-host", Options!.Host,
            "--rabbit-port", Options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--queue", queue
        };
        if (crashPoint is not null)
            arguments.AddRange(["--crash-point", crashPoint]);
        return ReliabilityHarnessProcess.Start(arguments);
    }

    private async Task<uint> GetReadyCountAsync(string queue, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = Options!.Host,
            Port = Options.Port,
            UserName = Options.UserName!,
            Password = Options.Password!
        };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        return (await channel.QueueDeclarePassiveAsync(queue, cancellationToken)).MessageCount;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadInboxAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var exists = new NpgsqlCommand($"SELECT to_regclass('{schema}.inbox_messages') IS NOT NULL", connection))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
                return entries;
        }

        await using var command = new NpgsqlCommand($"SELECT id, status FROM {schema}.inbox_messages", connection);
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
