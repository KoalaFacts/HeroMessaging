using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlInboxRecoveryTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task PendingMessageIsDeserializedAndProcessedAfterHostRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_recovery_{Guid.NewGuid():N}"
        };
        var message = new TestEvent();
        var messaging = new Mock<IHeroMessaging>();

        try
        {
            await using (var firstHost = CreateHost(options, messaging.Object))
            {
                var storage = firstHost.GetRequiredService<IInboxStorage>();
                var entry = await storage.AddAsync(message, new InboxOptions(), cancellationToken);
                Assert.NotNull(entry);
                var recovered = await storage.GetAsync(message.MessageId.ToString(), cancellationToken);
                Assert.IsType<TestEvent>(recovered?.Message);
            }

            await using (var secondHost = CreateHost(options, messaging.Object))
            {
                var storage = secondHost.GetRequiredService<IInboxStorage>();
                var pending = await storage.GetUnprocessedAsync(cancellationToken: cancellationToken);
                Assert.Contains(pending, entry => entry.Message is TestEvent @event && @event.MessageId == message.MessageId);

                var hosted = Assert.Single(secondHost.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                while ((await storage.GetAsync(message.MessageId.ToString(), timeout.Token))?.Status != InboxStatus.Processed)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);

                await hosted.StopAsync(cancellationToken);
            }

            messaging.Verify(service => service.PublishAsync(
                It.Is<IEvent>(@event => @event.MessageId == message.MessageId),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SlowHandlerIsNotQueuedAgainByTheSameHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_slow_{Guid.NewGuid():N}"
        };
        var message = new TestEvent();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEvent _, CancellationToken token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            });

        try
        {
            await using var host = CreateHost(options, messaging.Object);
            try
            {
                var storage = host.GetRequiredService<IInboxStorage>();
                await storage.AddAsync(message, new InboxOptions(), cancellationToken);
                var hosted = Assert.Single(host.GetServices<IHostedService>());
                await hosted.StartAsync(cancellationToken);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
                release.TrySetResult();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                while ((await storage.GetAsync(message.MessageId.ToString(), timeout.Token))?.Status != InboxStatus.Processed)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
                await hosted.StopAsync(cancellationToken);
                messaging.Verify(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult();
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider CreateHost(PostgreSqlStorageOptions options, IHeroMessaging messaging)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new HeroMessagingBuilder(services).WithInbox().Build();
        services.AddSingleton<IInboxStorage>(provider => new PostgreSqlInboxStorage(
            options, TimeProvider.System, provider.GetRequiredService<IJsonSerializer>()));
        services.AddSingleton(messaging);
        return services.BuildServiceProvider();
    }

    public sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }
}
