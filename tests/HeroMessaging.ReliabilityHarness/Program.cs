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
// Protocol on stdout (one line each): READY, ENQUEUED <id>, CRASHPOINT <point> <id>.
// The test kills the process at a CRASHPOINT; a line "stop" (or end of input) on stdin stops it cleanly.
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
    var transport = new CrashPointTransport(rabbit, settings.CrashPoint, WriteLine);
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

    await StartHostedAsync(provider);
}

WriteLine("READY");

string? command;
do
{
    command = await Console.In.ReadLineAsync();
}
while (command is not null && !string.Equals(command, "stop", StringComparison.Ordinal));

if (consumer is not null)
    await consumer.DisposeAsync();
foreach (var service in provider.GetServices<IHostedService>().Reverse())
    await service.StopAsync(CancellationToken.None);
await provider.DisposeAsync();
return 0;

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
        string? CrashPoint)
    {
        public const string OutboxPublisher = "outbox-publisher";
        public const string InboxConsumer = "inbox-consumer";

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
                InboxConsumer => crashPoint is null or InboxCrashPoint.BeforeInbox or InboxCrashPoint.AfterInbox,
                _ => throw new ArgumentException($"Unknown role '{role}'.")
            };
            if (!known)
                throw new ArgumentException($"Unknown crash point '{crashPoint}' for role '{role}'.");

            return new HarnessSettings(
                role,
                Required("--pg"),
                Required("--schema"),
                Required("--rabbit-host"),
                int.Parse(Required("--rabbit-port"), System.Globalization.CultureInfo.InvariantCulture),
                Required("--queue"),
                int.Parse(values.GetValueOrDefault("--enqueue", "0"), System.Globalization.CultureInfo.InvariantCulture),
                crashPoint);
        }
    }

    /// <summary>
    /// The consumer-side crash point: the first delivery stops before the Inbox admission, or after the Inbox
    /// entry is persisted but before the broker acknowledgement. In the second case the handler for that message
    /// also waits, so the entry stays unprocessed until the process is killed.
    /// </summary>
    internal sealed class InboxCrashPoint(string? crashPoint, Action<string> writeLine)
    {
        public const string BeforeInbox = "before-inbox";
        public const string AfterInbox = "after-inbox";
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

        public bool IsHalted(Guid messageId) => string.Equals(Volatile.Read(ref _haltedMessageId), messageId.ToString(), StringComparison.Ordinal);

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

            if (crashHere && crash.Point == InboxCrashPoint.AfterInbox)
                await crash.HaltAsync(message.MessageId);
            return admitted;
        }

        public IInboxProcessorMetrics GetMetrics() => inner.GetMetrics();
        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
        public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default) => inner.GetUnprocessedCountAsync(cancellationToken);
    }

    /// <summary>The business effect: one ledger row per handled message, tagged with this process ID.</summary>
    internal sealed class LedgerHandler(HarnessSettings settings, InboxCrashPoint crash) : IEventHandler<HarnessOrderPlaced>
    {
        public async Task HandleAsync(HarnessOrderPlaced @event, CancellationToken cancellationToken = default)
        {
            if (crash.IsHalted(@event.MessageId))
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {settings.Schema}.business_ledger (message_id, process_id) VALUES (@id, @process)", connection);
            command.Parameters.AddWithValue("id", @event.MessageId.ToString());
            command.Parameters.AddWithValue("process", Environment.ProcessId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Wraps the real transport and stops the first confirmed send at the declared point:
    /// before it reaches the broker, or after the broker confirmed it but before the Outbox entry is completed.
    /// The process then waits to be killed.
    /// </summary>
    internal sealed class CrashPointTransport(IConfirmedQueueTransport inner, string? crashPoint, Action<string> writeLine)
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
