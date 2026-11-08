using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Serialization.Json;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlRabbitMqOutboxPublishTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task TopicEntry_IsPublishedToEveryBoundQueueAndMarkedProcessed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithPostgreSqlAsync(async options =>
        {
            var exchange = CreateExchangeName();
            var exact = CreateQueueName();
            var wildcard = CreateQueueName();
            var unrelated = CreateQueueName();
            await ConfigureTopicAsync(exchange, (exact, "orders.created"), (wildcard, "orders.*"), (unrelated, "invoices.*"));
            var exactReceived = await ReceiveAsync(exact);
            var wildcardReceived = await ReceiveAsync(wildcard);
            var unrelatedReceived = await ReceiveAsync(unrelated);
            var message = TestMessageBuilder.CreateValidMessage("order created");

            await using var host = CreateHost(options);
            var hosted = Assert.Single(host.GetServices<IHostedService>());
            await hosted.StartAsync(cancellationToken);
            await host.GetRequiredService<IOutboxProcessor>().PublishToOutboxAsync(message,
                new OutboxOptions { Topic = exchange, RoutingKey = "orders.created" }, cancellationToken);

            foreach (var received in new[] { exactReceived, wildcardReceived })
            {
                var envelope = await received.First.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                Assert.Equal(message.MessageId.ToString(), envelope.MessageId);
                Assert.Equal(message.Content, new JsonMessageSerializer().Deserialize<TestMessage>(envelope.Body.Span)?.Content);
            }

            await WaitForAsync(async token =>
            {
                var processed = await host.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                    new OutboxQuery { Status = OutboxStatus.Processed, Limit = 10 }, token);
                return processed.Any(entry => entry.Message.MessageId == message.MessageId);
            }, cancellationToken);
            await hosted.StopAsync(cancellationToken);
            Assert.Empty(unrelatedReceived.All);
            Assert.Single(exactReceived.All);
            Assert.Single(wildcardReceived.All);
        });
    }

    [Fact]
    public async Task UnroutableTopicEntry_StaysPendingWithRouteErrorAndIsDeliveredOnceABindingExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithPostgreSqlAsync(async options =>
        {
            var exchange = CreateExchangeName();
            var queue = CreateQueueName();
            await ConfigureTopicAsync(exchange);
            var message = TestMessageBuilder.CreateValidMessage("nobody bound yet");

            await using var host = CreateHost(options);
            var hosted = Assert.Single(host.GetServices<IHostedService>());
            await hosted.StartAsync(cancellationToken);
            await host.GetRequiredService<IOutboxProcessor>().PublishToOutboxAsync(message,
                new OutboxOptions { Topic = exchange, RoutingKey = "orders.created", RetryDelay = TimeSpan.FromSeconds(1), MaxRetries = 30 },
                cancellationToken);

            OutboxEntry? retried = null;
            await WaitForAsync(async token =>
            {
                var pending = await host.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                    new OutboxQuery { Status = OutboxStatus.Pending, Limit = 10 }, token);
                retried = pending.FirstOrDefault(entry => entry.Message.MessageId == message.MessageId && entry.RetryCount >= 1);
                return retried is not null;
            }, cancellationToken);
            Assert.Contains(exchange, retried!.LastError, StringComparison.Ordinal);
            Assert.Equal(exchange, retried.Options.Topic);
            Assert.Equal("orders.created", retried.Options.RoutingKey);
            Assert.Null(retried.Options.Destination);

            await ConfigureTopicAsync(exchange, (queue, "orders.#"));
            var received = await ReceiveAsync(queue);
            var envelope = await received.First.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            Assert.Equal(message.MessageId.ToString(), envelope.MessageId);
            await hosted.StopAsync(cancellationToken);
        });
    }

    private async Task ConfigureTopicAsync(string exchange, params (string Queue, string RoutingKey)[] bindings)
    {
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition { Name = exchange, Type = ExchangeType.Topic, Durable = true });
        foreach (var (queue, routingKey) in bindings)
        {
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            topology.AddBinding(new BindingDefinition { SourceExchange = exchange, Destination = queue, RoutingKey = routingKey });
        }

        await Transport!.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);
    }

    private async Task<Received> ReceiveAsync(string queue)
    {
        var received = new Received();
        var consumer = await Transport!.SubscribeAsync(TransportAddress.Queue(queue),
            async (envelope, context, token) =>
            {
                await context.AcknowledgeAsync(token);
                received.All.Add(envelope);
                received.First.TrySetResult(envelope);
            }, cancellationToken: TestContext.Current.CancellationToken);
        _consumers.Add(consumer);
        return received;
    }

    private readonly List<IAsyncDisposable> _consumers = [];

    private sealed class Received
    {
        public ConcurrentBag<TransportEnvelope> All { get; } = [];
        public TaskCompletionSource<TransportEnvelope> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task WithPostgreSqlAsync(Func<PostgreSqlStorageOptions, Task> test)
    {
        PostgreSqlContainer? postgres = null;
        var connectionString = Environment.GetEnvironmentVariable("PostgreSql__ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            postgres = new PostgreSqlBuilder("postgres:17-alpine").WithPassword("postgres").Build();
            await postgres.StartAsync(TestContext.Current.CancellationToken);
            connectionString = postgres.GetConnectionString();
        }

        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = connectionString,
            Schema = $"outbox_publish_{Guid.NewGuid():N}"
        };
        try
        {
            await test(options);
        }
        finally
        {
            foreach (var consumer in _consumers)
                await consumer.DisposeAsync();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            if (postgres is not null)
                await postgres.DisposeAsync();
        }
    }

    private ServiceProvider CreateHost(PostgreSqlStorageOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMessageTransport>(Transport!);
        services.AddSingleton<IMessageSerializer>(new JsonMessageSerializer());
        services.AddHeroMessaging(builder => builder.WithOutbox().UsePostgreSqlOutbox(options));
        return services.BuildServiceProvider();
    }

    private static async Task WaitForAsync(Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!await condition(timeout.Token))
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }
}
