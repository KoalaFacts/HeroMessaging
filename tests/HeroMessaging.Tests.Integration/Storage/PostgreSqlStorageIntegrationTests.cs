using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.Storage;

public class PostgreSqlStorageIntegrationTests : DatabaseIntegrationTestBase
{
    private IOutboxStorage _outboxStorage = null!;
    private IInboxStorage _inboxStorage = null!;
    private IQueueStorage _queueStorage = null!;

    public PostgreSqlStorageIntegrationTests() : base(DatabaseProvider.PostgreSql)
    {
    }

    protected override async Task ConfigureServicesAsync(IServiceCollection services)
    {
        services.AddSingleton<IOutboxStorage>(new PostgreSqlOutboxStorage(ConnectionString));
        services.AddSingleton<IInboxStorage>(new PostgreSqlInboxStorage(ConnectionString));
        services.AddSingleton<IQueueStorage>(new PostgreSqlQueueStorage(ConnectionString));

        await base.ConfigureServicesAsync(services);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        
        _outboxStorage = GetRequiredService<IOutboxStorage>();
        _inboxStorage = GetRequiredService<IInboxStorage>();
        _queueStorage = GetRequiredService<IQueueStorage>();
    }

    protected override async Task InitializeDatabaseSchemaAsync()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var createTablesScript = @"
            CREATE TABLE IF NOT EXISTS outbox_messages (
                id UUID PRIMARY KEY,
                message_type VARCHAR(500) NOT NULL,
                payload BYTEA NOT NULL,
                destination VARCHAR(500),
                created_at TIMESTAMP NOT NULL,
                processed_at TIMESTAMP,
                retry_count INT NOT NULL DEFAULT 0,
                last_error TEXT,
                status VARCHAR(50) NOT NULL
            );

            CREATE TABLE IF NOT EXISTS inbox_messages (
                id UUID PRIMARY KEY,
                message_id VARCHAR(500) NOT NULL UNIQUE,
                message_type VARCHAR(500) NOT NULL,
                payload BYTEA NOT NULL,
                processed_at TIMESTAMP NOT NULL
            );

            CREATE TABLE IF NOT EXISTS queue_messages (
                id UUID PRIMARY KEY,
                queue_name VARCHAR(200) NOT NULL,
                message_type VARCHAR(500) NOT NULL,
                payload BYTEA NOT NULL,
                priority INT NOT NULL DEFAULT 0,
                created_at TIMESTAMP NOT NULL,
                visible_at TIMESTAMP NOT NULL,
                dequeue_count INT NOT NULL DEFAULT 0,
                last_error TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_queue_visibility 
            ON queue_messages(queue_name, visible_at, priority DESC);";

        using var command = new NpgsqlCommand(createTablesScript, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Should_Store_And_Retrieve_Outbox_Message()
    {
        // Arrange
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3, 4, 5 },
            Destination = "TestDestination",
            CreatedAt = DateTime.UtcNow,
            Status = OutboxMessageStatus.Pending
        };

        // Act
        await _outboxStorage.AddAsync(message);
        var retrieved = await _outboxStorage.GetPendingAsync(10);

        // Assert
        retrieved.Should().ContainSingle(m => m.Id == message.Id);
        var retrievedMessage = retrieved.First();
        retrievedMessage.MessageType.Should().Be(message.MessageType);
        retrievedMessage.Destination.Should().Be(message.Destination);
        retrievedMessage.Payload.Should().BeEquivalentTo(message.Payload);
    }

    [Fact]
    public async Task Should_Mark_Outbox_Message_As_Processed()
    {
        // Arrange
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            CreatedAt = DateTime.UtcNow,
            Status = OutboxMessageStatus.Pending
        };

        await _outboxStorage.AddAsync(message);

        // Act
        await _outboxStorage.MarkAsProcessedAsync(message.Id);
        var pending = await _outboxStorage.GetPendingAsync(10);

        // Assert
        pending.Should().NotContain(m => m.Id == message.Id);
    }

    [Fact]
    public async Task Should_Store_And_Check_Inbox_Message()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var message = new InboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            ProcessedAt = DateTime.UtcNow
        };

        // Act
        var isNew = await _inboxStorage.TryAddAsync(message);
        var isDuplicate = await _inboxStorage.TryAddAsync(message);

        // Assert
        isNew.Should().BeTrue();
        isDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Enqueue_And_Dequeue_Messages()
    {
        // Arrange
        var queueName = "test-queue";
        var message = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            MessageType = "TestMessage",
            Payload = new byte[] { 1, 2, 3 },
            Priority = 1,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _queueStorage.EnqueueAsync(message);
        var dequeued = await _queueStorage.DequeueAsync(queueName, TimeSpan.FromMinutes(1));

        // Assert
        dequeued.Should().NotBeNull();
        dequeued!.Id.Should().Be(message.Id);
        dequeued.MessageType.Should().Be(message.MessageType);
        dequeued.Payload.Should().BeEquivalentTo(message.Payload);
    }

    [Fact]
    public async Task Should_Respect_Message_Priority_In_Queue()
    {
        // Arrange
        var queueName = "priority-queue";
        var lowPriorityMessage = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            MessageType = "LowPriority",
            Payload = new byte[] { 1 },
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        };

        var highPriorityMessage = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            MessageType = "HighPriority",
            Payload = new byte[] { 2 },
            Priority = 10,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _queueStorage.EnqueueAsync(lowPriorityMessage);
        await _queueStorage.EnqueueAsync(highPriorityMessage);
        var firstDequeued = await _queueStorage.DequeueAsync(queueName, TimeSpan.FromMinutes(1));
        var secondDequeued = await _queueStorage.DequeueAsync(queueName, TimeSpan.FromMinutes(1));

        // Assert
        firstDequeued.Should().NotBeNull();
        firstDequeued!.MessageType.Should().Be("HighPriority");
        secondDequeued.Should().NotBeNull();
        secondDequeued!.MessageType.Should().Be("LowPriority");
    }

    [Fact]
    public async Task Should_Handle_Concurrent_Queue_Operations()
    {
        // Arrange
        var queueName = "concurrent-queue";
        var messageCount = 100;
        var tasks = new Task[messageCount];

        // Act - Enqueue messages concurrently
        for (int i = 0; i < messageCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                var message = new QueueMessage
                {
                    Id = Guid.NewGuid(),
                    QueueName = queueName,
                    MessageType = $"Message{index}",
                    Payload = new byte[] { (byte)index },
                    Priority = index % 5,
                    CreatedAt = DateTime.UtcNow
                };
                await _queueStorage.EnqueueAsync(message);
            });
        }

        await Task.WhenAll(tasks);

        // Dequeue all messages
        var dequeuedCount = 0;
        while (await _queueStorage.DequeueAsync(queueName, TimeSpan.FromMinutes(1)) != null)
        {
            dequeuedCount++;
        }

        // Assert
        dequeuedCount.Should().Be(messageCount);
    }
}