using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.ReliabilityHarness;
using HeroMessaging.Serialization.Json;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

// Separate OS process for the two-process reliability tests, in one of two roles:
// --role outbox-publisher (default): a PostgreSQL Outbox publisher delivering to a RabbitMQ queue.
// --role inbox-consumer: a RabbitMQ consumer admitting deliveries to a PostgreSQL Inbox whose handler
//   appends one row per handled message to the business ledger table <schema>.business_ledger.
//   With --effect idempotent the handler first records the message ID in <schema>.handled_messages in the same
//   transaction as the ledger row and skips the row when the ID is already there (a deduplicating handler), recording
//   the skip with this process ID in <schema>.skipped_effects.
// The publisher prints SENT <id> after each broker-confirmed send. With --send-delay-ms it then waits that long before
// completing the entry (lease still held), so competing publisher processes interleave. With --start manual it prints
// READY before starting its processors and starts them on a line "go" on stdin, printing STARTED.
// The consumer prints DUPLICATE <id> when the Inbox rejects a delivery as a duplicate (the delivery is still acknowledged).
// Protocol on stdout (one line each): READY, STARTED, ENQUEUED <id>, SENT <id>, CRASHPOINT <point> <id>, HANDLING <id>,
// EFFECT <id>, DUPLICATE <id>. The test kills the process at a CRASHPOINT; a line "stop" (or end of input) on stdin stops it cleanly.
var settings = HarnessSettings.Parse(args);
var lineLock = new object();
void WriteLine(string line)
{
    lock (lineLock)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }
}

var options = new RabbitMqTransportOptions
{
    Host = settings.RabbitHost,
    Port = settings.RabbitPort,
    VirtualHost = "/",
    UserName = "guest",
    Password = "guest",
    UsePublisherConfirms = true,
    ConnectionTimeout = TimeSpan.FromSeconds(30)
};
await using var rabbit = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);
await rabbit.ConnectAsync();
var storage = new PostgreSqlStorageOptions { ConnectionString = settings.ConnectionString, Schema = settings.Schema };
var serializer = new JsonMessageSerializer();

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddFilter(_ => false));
services.AddSingleton<IMessageSerializer>(serializer);
ITransportConsumer? consumer = null;
ServiceProvider provider;
if (settings.Role == HarnessSettings.InboxConsumer)
{
    var crash = new InboxCrashPoint(settings.CrashPoint, WriteLine);
    services.AddSingleton(crash);
    services.AddSingleton(settings);
    services.AddSingleton<IEventHandler<HarnessOrderPlaced>, LedgerHandler>();
    services.AddHeroMessaging(builder => builder.WithMediator().WithEventBus().WithInbox().UsePostgreSqlInbox(storage));
    provider = services.BuildServiceProvider();
    await StartHostedAsync(provider);
    consumer = await InboxTransportSubscription.SubscribeAsync<HarnessOrderPlaced>(
        rabbit, TransportAddress.Queue(settings.Queue),
        new CrashPointInbox(provider.GetRequiredService<IInboxProcessor>(), crash), serializer,
        consumerOptions: new ConsumerOptions { PrefetchCount = 1, ConcurrentMessageLimit = 1 });
}
else
{
    var transport = new CrashPointTransport(rabbit, settings.CrashPoint, settings.SendDelay, WriteLine);
    services.AddSingleton<IMessageTransport>(transport);
    services.AddHeroMessaging(builder => builder
        .WithOutbox()
        .UsePostgreSqlOutbox(storage)
        .ConfigureProcessing(processing => processing.ExternalOutboxMaxConcurrency = 1));
    provider = services.BuildServiceProvider();

    var outbox = provider.GetRequiredService<IOutboxProcessor>();
    for (var index = 0; index < settings.Enqueue; index++)
    {
        var message = new HarnessOrderPlaced { OrderNumber = index };
        await outbox.PublishToOutboxAsync(message, new OutboxOptions
        {
            Destination = settings.Queue,
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromSeconds(1)
        });
        WriteLine($"ENQUEUED {message.MessageId}");
        // Distinct created_at values keep the claim order equal to the enqueue order.
        await Task.Delay(TimeSpan.FromMilliseconds(20));
    }

    if (!settings.ManualStart)
        await StartHostedAsync(provider);
}

WriteLine("READY");
var started = !settings.ManualStart;
if (settings.ManualStart && await WaitForCommandAsync("go"))
{
    await StartHostedAsync(provider);
    started = true;
    WriteLine("STARTED");
}

await WaitForCommandAsync("stop");

if (consumer is not null)
    await consumer.DisposeAsync();
if (started)
{
    foreach (var service in provider.GetServices<IHostedService>().Reverse())
        await service.StopAsync(CancellationToken.None);
}
await provider.DisposeAsync();
return 0;

// True when the command arrived; false at end of input.
static async Task<bool> WaitForCommandAsync(string expected)
{
    string? command;
    do
    {
        command = await Console.In.ReadLineAsync();
    }
    while (command is not null && !string.Equals(command, expected, StringComparison.Ordinal));
    return command is not null;
}

static async Task StartHostedAsync(IServiceProvider provider)
{
    foreach (var service in provider.GetServices<IHostedService>())
        await service.StartAsync(CancellationToken.None);
}

namespace HeroMessaging.ReliabilityHarness
{
    /// <summary>Business message whose delivery count the test ledger checks.</summary>
    public sealed class HarnessOrderPlaced : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int OrderNumber { get; set; }
    }

    internal sealed record HarnessSettings(
        string Role,
        string ConnectionString,
        string Schema,
        string RabbitHost,
        int RabbitPort,
        string Queue,
        int Enqueue,
        string? CrashPoint,
        bool IdempotentEffect,
        bool ManualStart,
        TimeSpan SendDelay)
    {
        public const string OutboxPublisher = "outbox-publisher";
        public const string InboxConsumer = "inbox-consumer";
        public const string PlainEffect = "plain";
        public const string IdempotentEffectName = "idempotent";
        public const string ImmediateStart = "immediate";
        public const string ManualStartName = "manual";

        public static HarnessSettings Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index + 1 < args.Length; index += 2)
                values[args[index]] = args[index + 1];

            string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0
                ? value
                : throw new ArgumentException($"Missing required argument {name}.");

            var role = values.GetValueOrDefault("--role", OutboxPublisher);
            var crashPoint = values.GetValueOrDefault("--crash-point");
            var known = role switch
            {
                OutboxPublisher => crashPoint is null or CrashPointTransport.BeforePublish or CrashPointTransport.AfterConfirm,
                InboxConsumer => crashPoint is null or InboxCrashPoint.BeforeInbox or InboxCrashPoint.AfterInbox or InboxCrashPoint.AfterEffect,
                _ => throw new ArgumentException($"Unknown role '{role}'.")
            };
            if (!known)
                throw new ArgumentException($"Unknown crash point '{crashPoint}' for role '{role}'.");
            var effect = values.GetValueOrDefault("--effect", PlainEffect);
            if (effect is not (PlainEffect or IdempotentEffectName))
                throw new ArgumentException($"Unknown effect '{effect}'; use '{PlainEffect}' or '{IdempotentEffectName}'.");
            var start = values.GetValueOrDefault("--start", ImmediateStart);
            if (start is not (ImmediateStart or ManualStartName))
                throw new ArgumentException($"Unknown start mode '{start}'; use '{ImmediateStart}' or '{ManualStartName}'.");
            if (start == ManualStartName && role != OutboxPublisher)
                throw new ArgumentException($"--start {ManualStartName} applies only to role '{OutboxPublisher}'.");
            var sendDelayMs = int.Parse(values.GetValueOrDefault("--send-delay-ms", "0"), System.Globalization.CultureInfo.InvariantCulture);
            if (sendDelayMs < 0)
                throw new ArgumentException("--send-delay-ms must not be negative.");

            return new HarnessSettings(
                role,
                Required("--pg"),
                Required("--schema"),
                Required("--rabbit-host"),
                int.Parse(Required("--rabbit-port"), System.Globalization.CultureInfo.InvariantCulture),
                Required("--queue"),
                int.Parse(values.GetValueOrDefault("--enqueue", "0"), System.Globalization.CultureInfo.InvariantCulture),
                crashPoint,
                effect == IdempotentEffectName,
                start == ManualStartName,
                TimeSpan.FromMilliseconds(sendDelayMs));
        }
    }

    /// <summary>
    /// The consumer-side crash point: the first delivery stops before the Inbox admission, or after the Inbox
    /// entry is persisted but before the broker acknowledgement. After the admission (<c>after-inbox</c> and
    /// <c>after-effect</c>) the handler for that message also waits, so the entry stays unprocessed until the process
    /// is killed: with <c>after-inbox</c> before its business effect, with <c>after-effect</c> once the effect committed.
    /// </summary>
    internal sealed class InboxCrashPoint(string? crashPoint, Action<string> writeLine)
    {
        public const string BeforeInbox = "before-inbox";
        public const string AfterInbox = "after-inbox";
        public const string AfterEffect = "after-effect";
        private int _deliveries;
        private string? _haltedMessageId;

        public string? Point => crashPoint;

        public bool TryTake(Guid messageId)
        {
            if (crashPoint is null || Interlocked.Increment(ref _deliveries) != 1)
                return false;
            Volatile.Write(ref _haltedMessageId, messageId.ToString());
            return true;
        }

        public bool HoldsAfterEffect => crashPoint == AfterEffect;

        public bool IsHalted(Guid messageId) => string.Equals(Volatile.Read(ref _haltedMessageId), messageId.ToString(), StringComparison.Ordinal);

        /// <summary>Holds the halted message's handler until the process is killed, announcing that it started.</summary>
        public async Task HoldHandlerAsync(Guid messageId, CancellationToken cancellationToken)
        {
            writeLine($"HANDLING {messageId}");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        /// <summary>Holds the halted message's handler after its business effect committed, announcing it.</summary>
        public async Task HoldAfterEffectAsync(Guid messageId, CancellationToken cancellationToken)
        {
            writeLine($"EFFECT {messageId}");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        /// <summary>Reports a delivery the Inbox rejected as a duplicate.</summary>
        public void ReportDuplicate(Guid messageId) => writeLine($"DUPLICATE {messageId}");

        public async Task HaltAsync(Guid messageId)
        {
            writeLine($"CRASHPOINT {crashPoint} {messageId}");
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }
    }

    /// <summary>Wraps the real Inbox processor that the transport subscription admits deliveries to.</summary>
    internal sealed class CrashPointInbox(IInboxProcessor inner, InboxCrashPoint crash) : IInboxProcessor
    {
        public bool IsRunning => inner.IsRunning;

        public async Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
        {
            var crashHere = crash.TryTake(message.MessageId);
            if (crashHere && crash.Point == InboxCrashPoint.BeforeInbox)
                await crash.HaltAsync(message.MessageId);

            var admitted = await inner.ProcessIncomingAsync(message, options, cancellationToken);

            if (crashHere && crash.Point is InboxCrashPoint.AfterInbox or InboxCrashPoint.AfterEffect)
                await crash.HaltAsync(message.MessageId);
            if (!admitted)
                crash.ReportDuplicate(message.MessageId);
            return admitted;
        }

        public IInboxProcessorMetrics GetMetrics() => inner.GetMetrics();
        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
        public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default) => inner.GetUnprocessedCountAsync(cancellationToken);
    }

    /// <summary>
    /// The business effect: one ledger row per handled message, tagged with this process ID. A deduplicating handler
    /// (<c>--effect idempotent</c>) records the message ID in the same transaction and skips a message already recorded.
    /// </summary>
    internal sealed class LedgerHandler(HarnessSettings settings, InboxCrashPoint crash) : IEventHandler<HarnessOrderPlaced>
    {
        public async Task HandleAsync(HarnessOrderPlaced @event, CancellationToken cancellationToken = default)
        {
            var halted = crash.IsHalted(@event.MessageId);
            if (halted && !crash.HoldsAfterEffect)
                await crash.HoldHandlerAsync(@event.MessageId, cancellationToken);

            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var firstTime = !settings.IdempotentEffect || await RecordHandledAsync(connection, transaction, @event.MessageId, cancellationToken);
            if (firstTime)
            {
                await using var command = new NpgsqlCommand(
                    $"INSERT INTO {settings.Schema}.business_ledger (message_id, process_id) VALUES (@id, @process)", connection, transaction);
                command.Parameters.AddWithValue("id", @event.MessageId.ToString());
                command.Parameters.AddWithValue("process", Environment.ProcessId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var skipped = new NpgsqlCommand(
                    $"INSERT INTO {settings.Schema}.skipped_effects (message_id, process_id) VALUES (@id, @process)", connection, transaction);
                skipped.Parameters.AddWithValue("id", @event.MessageId.ToString());
                skipped.Parameters.AddWithValue("process", Environment.ProcessId);
                await skipped.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            if (halted && crash.HoldsAfterEffect)
                await crash.HoldAfterEffectAsync(@event.MessageId, cancellationToken);
        }

        private async Task<bool> RecordHandledAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid messageId, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {settings.Schema}.handled_messages (message_id) VALUES (@id) ON CONFLICT (message_id) DO NOTHING", connection, transaction);
            command.Parameters.AddWithValue("id", messageId.ToString());
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
    }

    /// <summary>
    /// Wraps the real transport and stops the first confirmed send at the declared point:
    /// before it reaches the broker, or after the broker confirmed it but before the Outbox entry is completed.
    /// The process then waits to be killed. Every other confirmed send is reported as <c>SENT &lt;id&gt;</c>, followed by
    /// the configured send delay.
    /// </summary>
    internal sealed class CrashPointTransport(IConfirmedQueueTransport inner, string? crashPoint, TimeSpan sendDelay, Action<string> writeLine)
        : IConfirmedQueueTransport
    {
        public const string BeforePublish = "before-publish";
        public const string AfterConfirm = "after-confirm";
        private int _sends;

        public string Name => inner.Name;
        public TransportState State => inner.State;

        public event EventHandler<TransportStateChangedEventArgs>? StateChanged
        {
            add => inner.StateChanged += value;
            remove => inner.StateChanged -= value;
        }

        public event EventHandler<TransportErrorEventArgs>? Error
        {
            add => inner.Error += value;
            remove => inner.Error -= value;
        }

        public async Task SendConfirmedAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var crashHere = crashPoint is not null && Interlocked.Increment(ref _sends) == 1;
            if (crashHere && crashPoint == BeforePublish)
                await HaltAsync(BeforePublish, envelope.MessageId);

            await inner.SendConfirmedAsync(destination, envelope, cancellationToken);

            if (crashHere && crashPoint == AfterConfirm)
                await HaltAsync(AfterConfirm, envelope.MessageId);

            writeLine($"SENT {envelope.MessageId}");
            if (sendDelay > TimeSpan.Zero)
                await Task.Delay(sendDelay, cancellationToken);
        }

        private async Task HaltAsync(string point, string messageId)
        {
            writeLine($"CRASHPOINT {point} {messageId}");
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => inner.ConnectAsync(cancellationToken);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => inner.DisconnectAsync(cancellationToken);
        public Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default)
            => inner.SendAsync(destination, envelope, cancellationToken);
        public Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default)
            => inner.PublishAsync(topic, envelope, cancellationToken);
        public Task<ITransportConsumer> SubscribeAsync(TransportAddress source,
            Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
            ConsumerOptions? options = null, CancellationToken cancellationToken = default)
            => inner.SubscribeAsync(source, handler, options, cancellationToken);
        public Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken = default)
            => inner.ConfigureTopologyAsync(topology, cancellationToken);
        public Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default) => inner.GetHealthAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
