using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.Serialization.Json;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlRabbitMqInboxRecoveryTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task DatabaseFailureRequeuesAndDuplicateDeliveryIsAcknowledgedOncePersisted()
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
            Schema = $"inbox_broker_{Guid.NewGuid():N}",
            AutoCreateTables = false
        };
        var queue = CreateQueueName();
        var message = new TestEvent();
        var serializer = new JsonMessageSerializer();
        var envelope = new TransportEnvelope
        {
            MessageId = message.MessageId.ToString(),
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            ContentType = serializer.ContentType,
            Body = await serializer.SerializeAsync(message, cancellationToken)
        };
        var messaging = new Mock<IHeroMessaging>();

        try
        {
            var topology = new TransportTopology();
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            await Transport!.ConfigureTopologyAsync(topology, cancellationToken);
            await using var host = CreateHost(options, messaging.Object);
            var inbox = host.GetRequiredService<IInboxProcessor>();
            await using (var failingConsumer = await InboxTransportSubscription.SubscribeAsync<TestEvent>(
                Transport, TransportAddress.Queue(queue), inbox, serializer, cancellationToken: cancellationToken))
            {
                await Transport.SendConfirmedAsync(TransportAddress.Queue(queue), envelope, cancellationToken);
                await WaitForAsync(() => failingConsumer.GetMetrics().MessagesFailed > 0, cancellationToken);
                await failingConsumer.StopAsync(cancellationToken);
            }

            var readyOptions = new PostgreSqlStorageOptions { ConnectionString = connectionString, Schema = options.Schema };
            var readyStorage = new PostgreSqlInboxStorage(readyOptions, TimeProvider.System,
                host.GetRequiredService<IJsonSerializer>());
            Assert.Empty(await readyStorage.GetUnprocessedAsync(cancellationToken: cancellationToken));

            await using var recoveredConsumer = await InboxTransportSubscription.SubscribeAsync<TestEvent>(
                Transport, TransportAddress.Queue(queue), inbox, serializer, cancellationToken: cancellationToken);
            await WaitForAsync(() => recoveredConsumer.GetMetrics().MessagesProcessed >= 1, cancellationToken);
            Assert.NotNull(await readyStorage.GetAsync(message.MessageId.ToString(), cancellationToken));

            await Transport.SendConfirmedAsync(TransportAddress.Queue(queue), envelope, cancellationToken);
            await WaitForAsync(() => recoveredConsumer.GetMetrics().MessagesProcessed >= 2, cancellationToken);
            await using (var verification = new NpgsqlConnection(connectionString))
            {
                await verification.OpenAsync(cancellationToken);
                await using var count = new NpgsqlCommand(
                    $"SELECT COUNT(*) FROM {options.Schema}.{options.InboxTableName} WHERE id = @id", verification);
                count.Parameters.AddWithValue("id", message.MessageId.ToString());
                Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken)));
            }

            var hosted = Assert.Single(host.GetServices<IHostedService>());
            await hosted.StartAsync(cancellationToken);
            await WaitForAsync(async token =>
                (await readyStorage.GetAsync(message.MessageId.ToString(), token))?.Status == InboxStatus.Processed,
                cancellationToken);
            await hosted.StopAsync(cancellationToken);
            messaging.Verify(service => service.PublishAsync(
                It.Is<IEvent>(value => value.MessageId == message.MessageId), It.IsAny<CancellationToken>()), Times.Once);
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

    private static ServiceProvider CreateHost(PostgreSqlStorageOptions options, IHeroMessaging messaging)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeroMessaging(builder => builder.WithInbox().UsePostgreSqlInbox(options));
        services.AddSingleton(messaging);
        return services.BuildServiceProvider();
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!condition())
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }

    private static async Task WaitForAsync(Func<CancellationToken, Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await condition(timeout.Token))
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }

    public sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
