using System.Collections.Concurrent;
using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Two separate OS processes deliver a PostgreSQL Outbox to RabbitMQ. The first is killed at a declared
/// crash point; the second takes over once the dead lease expires. The business ledger is the count of
/// deliveries per message on the destination queue.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlRabbitMqOutboxProcessCrashTests : RabbitMqIntegrationTestBase
{
    private const int MessageCount = 3;
    private static readonly TimeSpan TakeoverTimeout = TimeSpan.FromSeconds(150);

    [Theory]
    [InlineData("before-publish", 1)]
    [InlineData("after-confirm", 2)]
    public async Task PublisherKilledAtCrashPoint_SecondProcessDeliversEveryEntry(string crashPoint, int crashedDeliveries)
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

        var schema = $"outbox_crash_{Guid.NewGuid():N}";
        var queue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken);

        var ledger = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        await using var consumer = await Transport.SubscribeAsync(TransportAddress.Queue(queue),
            async (envelope, context, token) =>
            {
                ledger.AddOrUpdate(envelope.MessageId, 1, (_, count) => count + 1);
                await context.AcknowledgeAsync(token);
            }, cancellationToken: cancellationToken);

        try
        {
            string crashedId;
            var enqueued = new List<string>();
            using (var first = HarnessProcess.Start(connectionString, schema, Options!, queue, MessageCount, crashPoint))
            {
                string line;
                while (!(line = await first.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken)).StartsWith("CRASHPOINT ", StringComparison.Ordinal))
                {
                    if (line.StartsWith("ENQUEUED ", StringComparison.Ordinal))
                        enqueued.Add(line["ENQUEUED ".Length..]);
                }

                Assert.Equal($"CRASHPOINT {crashPoint}", line[..line.LastIndexOf(' ')]);
                crashedId = line[(line.LastIndexOf(' ') + 1)..];
                first.Kill();
            }

            Assert.Equal(MessageCount, enqueued.Count);
            Assert.Equal(enqueued[0], crashedId);
            var dead = await ReadEntriesAsync(connectionString, schema, cancellationToken);
            Assert.Equal("Processing", dead[crashedId]);
            Assert.All(enqueued.Skip(1), id => Assert.Equal("Pending", dead[id]));

            using var second = HarnessProcess.Start(connectionString, schema, Options!, queue, enqueue: 0, crashPoint: null);
            Assert.Equal("READY", await second.ReadLineAsync(TimeSpan.FromSeconds(60), cancellationToken));
            var expected = enqueued.ToDictionary(id => id, id => id == crashedId ? crashedDeliveries : 1, StringComparer.Ordinal);
            var entries = new Dictionary<string, string>();
            await WaitForAsync(async token =>
            {
                entries = await ReadEntriesAsync(connectionString, schema, token);
                return entries.Values.All(status => status == "Processed")
                    && expected.All(pair => ledger.GetValueOrDefault(pair.Key) >= pair.Value);
            }, TakeoverTimeout, () => $"Second process did not deliver every entry within {TakeoverTimeout}. Outbox: "
                + string.Join(", ", entries.Select(pair => $"{pair.Key}={pair.Value}"))
                + "; ledger: " + string.Join(", ", ledger.Select(pair => $"{pair.Key}={pair.Value}")), cancellationToken);

            // Give a stray duplicate time to arrive before the ledger is checked.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            Assert.Equal(0, await second.StopAsync(TimeSpan.FromSeconds(30), cancellationToken));
            Assert.Equal(
                expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                ledger.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            if (postgres is not null)
            {
                await postgres.StopAsync(CancellationToken.None);
                await postgres.DisposeAsync();
            }
        }
    }

    private static async Task<Dictionary<string, string>> ReadEntriesAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT COALESCE(payload->>'MessageId', payload->>'messageId'), status FROM {schema}.outbox_messages", connection);
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries[reader.GetString(0)] = reader.GetString(1);
        return entries;
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

    /// <summary>The outbox publisher from HeroMessaging.ReliabilityHarness, run as its own OS process.</summary>
    private sealed class HarnessProcess : IDisposable
    {
        private readonly Process _process;

        private HarnessProcess(Process process)
        {
            _process = process;
        }

        public static HarnessProcess Start(string connectionString, string schema, RabbitMqTransportOptions rabbit,
            string queue, int enqueue, string? crashPoint)
        {
            var harness = Path.Join(AppContext.BaseDirectory, "HeroMessaging.ReliabilityHarness.dll");
            Assert.True(File.Exists(harness), $"Reliability harness not found at {harness}.");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false
            };
            foreach (var argument in new[]
                     {
                         harness, "--pg", connectionString, "--schema", schema, "--rabbit-host", rabbit.Host,
                         "--rabbit-port", rabbit.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                         "--queue", queue, "--enqueue", enqueue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                     })
                start.ArgumentList.Add(argument);
            if (crashPoint is not null)
            {
                start.ArgumentList.Add("--crash-point");
                start.ArgumentList.Add(crashPoint);
            }

            return new HarnessProcess(Process.Start(start) ?? throw new InvalidOperationException("Could not start the reliability harness."));
        }

        public async Task<string> ReadLineAsync(TimeSpan limit, CancellationToken cancellationToken)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(limit, cancellationToken);
            return line ?? throw new InvalidOperationException($"Reliability harness exited with {(_process.WaitForExit(5000) ? _process.ExitCode : -1)} before the expected output.");
        }

        public void Kill()
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }

        public async Task<int> StopAsync(TimeSpan limit, CancellationToken cancellationToken)
        {
            await _process.StandardInput.WriteLineAsync("stop");
            await _process.StandardInput.FlushAsync(cancellationToken);
            await _process.WaitForExitAsync(cancellationToken).WaitAsync(limit, cancellationToken);
            return _process.ExitCode;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }
}
