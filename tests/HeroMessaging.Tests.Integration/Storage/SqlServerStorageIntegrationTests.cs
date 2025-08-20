using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Tests.Integration.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HeroMessaging.Tests.Integration.Storage;

public class SqlServerStorageIntegrationTests : DatabaseIntegrationTestBase
{
    private IOutboxStorage _outboxStorage = null!;
    private IInboxStorage _inboxStorage = null!;
    private IQueueStorage _queueStorage = null!;

    public SqlServerStorageIntegrationTests() : base(DatabaseProvider.SqlServer)
    {
    }

    protected override async Task ConfigureServicesAsync(IServiceCollection services)
    {
        services.AddSingleton<IOutboxStorage>(new SqlServerOutboxStorage(ConnectionString));
        services.AddSingleton<IInboxStorage>(new SqlServerInboxStorage(ConnectionString));
        services.AddSingleton<IQueueStorage>(new SqlServerQueueStorage(ConnectionString));

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
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var createTablesScript = @"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='OutboxMessages' AND xtype='U')
            CREATE TABLE OutboxMessages (
                Id UNIQUEIDENTIFIER PRIMARY KEY,
                MessageType NVARCHAR(500) NOT NULL,
                Payload NVARCHAR(MAX) NOT NULL,
                Destination NVARCHAR(500),
                CreatedAt DATETIME2 NOT NULL,
                ProcessedAt DATETIME2,
                RetryCount INT NOT NULL DEFAULT 0,
                LastError NVARCHAR(MAX),
                Status NVARCHAR(50) NOT NULL
            );

            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InboxMessages' AND xtype='U')
            CREATE TABLE InboxMessages (
                Id UNIQUEIDENTIFIER PRIMARY KEY,
                MessageId NVARCHAR(500) NOT NULL,
                MessageType NVARCHAR(500) NOT NULL,
                Payload NVARCHAR(MAX) NOT NULL,
                ProcessedAt DATETIME2 NOT NULL,
                UNIQUE(MessageId)
            );

            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='QueueMessages' AND xtype='U')
            CREATE TABLE QueueMessages (
                Id UNIQUEIDENTIFIER PRIMARY KEY,
                QueueName NVARCHAR(200) NOT NULL,
                MessageType NVARCHAR(500) NOT NULL,
                Payload NVARCHAR(MAX) NOT NULL,
                Priority INT NOT NULL DEFAULT 0,
                CreatedAt DATETIME2 NOT NULL,
                VisibleAt DATETIME2 NOT NULL,
                DequeueCount INT NOT NULL DEFAULT 0,
                LastError NVARCHAR(MAX),
                INDEX IX_Queue_Visibility (QueueName, VisibleAt, Priority DESC)
            );";

        using var command = new SqlCommand(createTablesScript, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Should_Store_And_Retrieve_Outbox_Message()
    {
        // Arrange
        var message = new TestMessage { Id = Guid.NewGuid(), Data = "Test Data" };
        var options = new OutboxOptions { Destination = "TestDestination" };

        // Act
        var entry = await _outboxStorage.Add(message, options);
        var retrieved = await _outboxStorage.GetPending(10);

        // Assert
        Assert.Single(retrieved, m => m.Id == entry.Id);
        var retrievedEntry = retrieved.First(m => m.Id == entry.Id);
        Assert.Equal(options.Destination, retrievedEntry.Options.Destination);
        Assert.Equal(OutboxStatus.Pending, retrievedEntry.Status);
    }

    [Fact]
    public async Task Should_Mark_Outbox_Message_As_Processed()
    {
        // Arrange
        var message = new TestMessage { Id = Guid.NewGuid(), Data = "Test Data" };
        var options = new OutboxOptions();
        var entry = await _outboxStorage.Add(message, options);

        // Act
        var result = await _outboxStorage.MarkProcessed(entry.Id);
        var pending = await _outboxStorage.GetPending(10);

        // Assert
        Assert.True(result);
        Assert.DoesNotContain(pending, m => m.Id == entry.Id);
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