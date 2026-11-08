using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.ReliabilityHarness;
using HeroMessaging.Serialization.Json;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Outbox publisher process for the two-process reliability tests.
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
var transport = new CrashPointTransport(rabbit, settings.CrashPoint, WriteLine);

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddFilter(_ => false));
services.AddSingleton<IMessageTransport>(transport);
services.AddSingleton<IMessageSerializer>(new JsonMessageSerializer());
services.AddHeroMessaging(builder => builder
    .WithOutbox()
    .UsePostgreSqlOutbox(new PostgreSqlStorageOptions { ConnectionString = settings.ConnectionString, Schema = settings.Schema })
    .ConfigureProcessing(processing => processing.ExternalOutboxMaxConcurrency = 1));
await using var provider = services.BuildServiceProvider();

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

var hosted = provider.GetServices<IHostedService>().ToArray();
foreach (var service in hosted)
    await service.StartAsync(CancellationToken.None);
WriteLine("READY");

string? command;
do
{
    command = await Console.In.ReadLineAsync();
}
while (command is not null && !string.Equals(command, "stop", StringComparison.Ordinal));

foreach (var service in hosted.AsEnumerable().Reverse())
    await service.StopAsync(CancellationToken.None);
return 0;

namespace HeroMessaging.ReliabilityHarness
{
    /// <summary>Business message whose delivery count the test ledger checks.</summary>
    public sealed class HarnessOrderPlaced : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int OrderNumber { get; set; }
    }

    internal sealed record HarnessSettings(
        string ConnectionString,
        string Schema,
        string RabbitHost,
        int RabbitPort,
        string Queue,
        int Enqueue,
        string? CrashPoint)
    {
        public static HarnessSettings Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index + 1 < args.Length; index += 2)
                values[args[index]] = args[index + 1];

            string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0
                ? value
                : throw new ArgumentException($"Missing required argument {name}.");

            var crashPoint = values.GetValueOrDefault("--crash-point");
            if (crashPoint is not null && crashPoint is not (CrashPointTransport.BeforePublish or CrashPointTransport.AfterConfirm))
                throw new ArgumentException($"Unknown crash point '{crashPoint}'.");

            return new HarnessSettings(
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
