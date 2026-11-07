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
public sealed class PostgreSqlRabbitMqOutboxRecoveryTests : RabbitMqIntegrationTestBase
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task UnroutableDelivery_RetriesAfterHostRestartAndQueueRecovery(int messageCount)
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

        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = connectionString,
            Schema = $"outbox_recovery_{Guid.NewGuid():N}"
        };
        var queue = CreateQueueName();
        var messages = Enumerable.Range(0, messageCount)
            .Select(index => TestMessageBuilder.CreateValidMessage($"recover after restart {index}"))
            .ToArray();

        try
        {
            await using (var firstHost = CreateHost(options))
            {
                var hosted = Assert.Single(firstHost.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                foreach (var message in messages)
                    await firstHost.GetRequiredService<IOutboxProcessor>().PublishToOutboxAsync(message,
                        new OutboxOptions { Destination = queue, RetryDelay = TimeSpan.FromSeconds(2), MaxRetries = 20 }, cancellationToken);

                await WaitForAsync(async token =>
                {
                    var entries = await firstHost.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                        new OutboxQuery { Status = OutboxStatus.Pending, Limit = 10 }, token);
                    return messages.All(message => entries.Any(entry =>
                        entry.Message.MessageId == message.MessageId && entry.RetryCount >= 1));
                }, cancellationToken);

                await hosted.StopAsync(cancellationToken);
            }

            var topology = new TransportTopology();
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            await Transport!.ConfigureTopologyAsync(topology, cancellationToken);
            var received = new ConcurrentDictionary<Guid, TransportEnvelope>();
            var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var consumer = await Transport.SubscribeAsync(TransportAddress.Queue(queue),
                async (envelope, context, token) =>
                {
                    await context.AcknowledgeAsync(token);
                    if (Guid.TryParse(envelope.MessageId, out var id) && received.TryAdd(id, envelope)
                        && received.Count == messageCount)
                        allReceived.TrySetResult();
                }, cancellationToken: cancellationToken);

            await using (var secondHost = CreateHost(options))
            {
                var hosted = Assert.Single(secondHost.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                foreach (var message in messages)
                {
                    Assert.True(received.TryGetValue(message.MessageId, out var envelope));
                    Assert.Equal("application/json", envelope.ContentType);
                    Assert.Equal(message.Content, new JsonMessageSerializer().Deserialize<TestMessage>(envelope.Body.Span)?.Content);
                }

                await WaitForAsync(async token =>
                {
                    var entries = await secondHost.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                        new OutboxQuery { Status = OutboxStatus.Processed, Limit = 10 }, token);
                    return messages.All(message => entries.Any(entry => entry.Message.MessageId == message.MessageId));
                }, cancellationToken);
                await hosted.StopAsync(cancellationToken);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            if (postgres is not null)
            {
                await postgres.StopAsync(CancellationToken.None);
                await postgres.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task TopicDestination_RetriesWhileUnboundThenReachesEveryBoundQueue()
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

        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = connectionString,
            Schema = $"outbox_topic_{Guid.NewGuid():N}"
        };
        var exchange = CreateExchangeName();
        var queues = new[] { CreateQueueName(), CreateQueueName() };
        var message = TestMessageBuilder.CreateValidMessage("topic outbox");
        var exchangeOnly = new TransportTopology();
        exchangeOnly.AddExchange(new ExchangeDefinition { Name = exchange, Type = ExchangeType.Topic, Durable = true });
        await Transport!.ConfigureTopologyAsync(exchangeOnly, cancellationToken);

        try
        {
            await using (var firstHost = CreateHost(options))
            {
                var hosted = Assert.Single(firstHost.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                try
                {
                    await firstHost.GetRequiredService<IOutboxProcessor>().PublishToOutboxAsync(message,
                        new OutboxOptions { Destination = $"topic:{exchange}", RetryDelay = TimeSpan.FromSeconds(1), MaxRetries = 20 }, cancellationToken);

                    await WaitForAsync(async token =>
                    {
                        var entries = await firstHost.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                            new OutboxQuery { Status = OutboxStatus.Pending, Limit = 10 }, token);
                        return entries.Any(entry => entry.Message.MessageId == message.MessageId && entry.RetryCount >= 1
                            && entry.LastError?.Contains(exchange, StringComparison.Ordinal) == true);
                    }, cancellationToken);
                }
                finally
                {
                    await hosted.StopAsync(CancellationToken.None);
                }
            }

            // Bind both queues while no worker runs, so a retry cannot land between the two bindings.
            var bound = new TransportTopology();
            foreach (var queue in queues)
            {
                bound.AddQueue(new QueueDefinition { Name = queue, Durable = true });
                bound.AddBinding(new BindingDefinition { SourceExchange = exchange, Destination = queue, RoutingKey = "#" });
            }
            await Transport.ConfigureTopologyAsync(bound, cancellationToken);
            var received = new List<Task<TransportEnvelope>>();
            var consumers = new List<ITransportConsumer>();
            try
            {
                foreach (var queue in queues)
                {
                    var arrived = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
                    consumers.Add(await Transport.SubscribeAsync(TransportAddress.Queue(queue), async (envelope, context, token) =>
                    {
                        await context.AcknowledgeAsync(token);
                        arrived.TrySetResult(envelope);
                    }, cancellationToken: cancellationToken));
                    received.Add(arrived.Task);
                }

                await using var secondHost = CreateHost(options);
                var hosted = Assert.Single(secondHost.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                try
                {
                    foreach (var arrival in received)
                    {
                        var envelope = await arrival.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                        Assert.Equal(message.MessageId.ToString(), envelope.MessageId);
                        Assert.Equal(message.Content, new JsonMessageSerializer().Deserialize<TestMessage>(envelope.Body.Span)?.Content);
                    }

                    await WaitForAsync(async token =>
                    {
                        var entries = await secondHost.GetRequiredService<IOutboxStorage>().GetPendingAsync(
                            new OutboxQuery { Status = OutboxStatus.Processed, Limit = 10 }, token);
                        return entries.Any(entry => entry.Message.MessageId == message.MessageId);
                    }, cancellationToken);
                }
                finally
                {
                    await hosted.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                foreach (var consumer in consumers)
                    await consumer.DisposeAsync();
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            if (postgres is not null)
            {
                await postgres.StopAsync(CancellationToken.None);
                await postgres.DisposeAsync();
            }
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
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await condition(timeout.Token))
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }
}
