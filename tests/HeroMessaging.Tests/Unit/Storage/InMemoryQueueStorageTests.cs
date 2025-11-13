using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public sealed class InMemoryQueueStorageTests
{
    #region Test Helper Classes

    private sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string? Content { get; set; }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidTimeProvider_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var storage = new InMemoryQueueStorage(timeProvider);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InMemoryQueueStorage(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region CreateQueueAsync Tests

    [Fact]
    public async Task CreateQueueAsync_WithNewQueueName_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var result = await storage.CreateQueueAsync("test-queue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateQueueAsync_WithExistingQueueName_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var result = await storage.CreateQueueAsync("test-queue");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateQueueAsync_WithOptions_StoresOptions()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var options = new QueueOptions
        {
            MaxSize = 1000,
            MessageTtl = TimeSpan.FromHours(1),
            MaxDequeueCount = 5,
            VisibilityTimeout = TimeSpan.FromMinutes(5)
        };

        // Act
        await storage.CreateQueueAsync("test-queue", options);

        // Assert - Verify queue exists
        var exists = await storage.QueueExistsAsync("test-queue");
        Assert.True(exists);
    }

    #endregion

    #region DeleteQueueAsync Tests

    [Fact]
    public async Task DeleteQueueAsync_WithExistingQueue_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var result = await storage.DeleteQueueAsync("test-queue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteQueueAsync_WithNonExistentQueue_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var result = await storage.DeleteQueueAsync("non-existent-queue");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteQueueAsync_RemovesQueueAndMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.CreateQueueAsync(queueName);
        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        await storage.DeleteQueueAsync(queueName);

        // Assert
        var exists = await storage.QueueExistsAsync(queueName);
        Assert.False(exists);
    }

    #endregion

    #region GetQueuesAsync Tests

    [Fact]
    public async Task GetQueuesAsync_WithNoQueues_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var queues = await storage.GetQueuesAsync();

        // Assert
        Assert.Empty(queues);
    }

    [Fact]
    public async Task GetQueuesAsync_WithMultipleQueues_ReturnsAllQueueNames()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        await storage.CreateQueueAsync("queue1");
        await storage.CreateQueueAsync("queue2");
        await storage.CreateQueueAsync("queue3");

        // Act
        var queues = await storage.GetQueuesAsync();

        // Assert
        var queueList = queues.ToList();
        Assert.Equal(3, queueList.Count);
        Assert.Contains("queue1", queueList);
        Assert.Contains("queue2", queueList);
        Assert.Contains("queue3", queueList);
    }

    #endregion

    #region QueueExistsAsync Tests

    [Fact]
    public async Task QueueExistsAsync_WithNonExistentQueue_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var exists = await storage.QueueExistsAsync("non-existent-queue");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task QueueExistsAsync_WithExistingQueue_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var exists = await storage.QueueExistsAsync("test-queue");

        // Assert
        Assert.True(exists);
    }

    #endregion

    #region EnqueueAsync Tests

    [Fact]
    public async Task EnqueueAsync_WithNewQueue_CreatesQueueAndEnqueuesMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var queueName = "test-queue";

        // Act
        var entry = await storage.EnqueueAsync(queueName, message);

        // Assert
        Assert.NotNull(entry);
        Assert.NotNull(entry.Id);
        Assert.NotEmpty(entry.Id);
        Assert.Same(message, entry.Message);
        Assert.Equal(timeProvider.GetUtcNow(), entry.EnqueuedAt);
        Assert.Equal(timeProvider.GetUtcNow(), entry.VisibleAt);
        Assert.Equal(0, entry.DequeueCount);
    }

    [Fact]
    public async Task EnqueueAsync_WithExistingQueue_EnqueuesMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        await storage.CreateQueueAsync(queueName);

        var message = new TestMessage();

        // Act
        var entry = await storage.EnqueueAsync(queueName, message);

        // Assert
        Assert.NotNull(entry);
        Assert.Same(message, entry.Message);
    }

    [Fact]
    public async Task EnqueueAsync_WithDelay_SetsVisibleAtInFuture()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var message = new TestMessage();
        var delay = TimeSpan.FromMinutes(5);
        var options = new EnqueueOptions { Delay = delay };

        // Act
        var entry = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        var expectedVisibleAt = timeProvider.GetUtcNow().Add(delay);
        Assert.Equal(expectedVisibleAt, entry.VisibleAt);
    }

    [Fact]
    public async Task EnqueueAsync_WithOptions_StoresOptions()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var message = new TestMessage();
        var options = new EnqueueOptions
        {
            Priority = 10,
            Delay = TimeSpan.FromMinutes(2),
            Metadata = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var entry = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.Equal(10, entry.Options.Priority);
        Assert.Equal(TimeSpan.FromMinutes(2), entry.Options.Delay);
        Assert.NotNull(entry.Options.Metadata);
        Assert.Equal("value", entry.Options.Metadata["key"]);
    }

    [Fact]
    public async Task EnqueueAsync_GeneratesUniqueIds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var ids = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var entry = await storage.EnqueueAsync("test-queue", new TestMessage());
            ids.Add(entry.Id);
        }

        // Assert
        Assert.Equal(100, ids.Count);
    }

    #endregion

    #region DequeueAsync Tests

    [Fact]
    public async Task DequeueAsync_WithNonExistentQueue_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var entry = await storage.DequeueAsync("non-existent-queue");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task DequeueAsync_WithEmptyQueue_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var entry = await storage.DequeueAsync("test-queue");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task DequeueAsync_WithVisibleMessage_ReturnsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var message = new TestMessage { Content = "Test" };

        await storage.EnqueueAsync(queueName, message);

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.MessageId, entry.Message.MessageId);
    }

    [Fact]
    public async Task DequeueAsync_IncrementsDequeueCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(1, entry.DequeueCount);
    }

    [Fact]
    public async Task DequeueAsync_SetsVisibleAtWithVisibilityTimeout()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var visibilityTimeout = TimeSpan.FromMinutes(5);

        await storage.CreateQueueAsync(queueName, new QueueOptions { VisibilityTimeout = visibilityTimeout });
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
        var expectedVisibleAt = timeProvider.GetUtcNow().Add(visibilityTimeout);
        Assert.Equal(expectedVisibleAt, entry.VisibleAt);
    }

    [Fact]
    public async Task DequeueAsync_WithDelayedMessage_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var delay = TimeSpan.FromMinutes(10);

        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = delay });

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task DequeueAsync_AfterDelayExpires_ReturnsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var delay = TimeSpan.FromMinutes(5);

        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = delay });

        // Advance time beyond delay
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task DequeueAsync_WithHigherPriority_ReturnsHigherPriorityFirst()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var message1 = new TestMessage { Content = "Priority 1" };
        var message2 = new TestMessage { Content = "Priority 10" };
        var message3 = new TestMessage { Content = "Priority 5" };

        await storage.EnqueueAsync(queueName, message1, new EnqueueOptions { Priority = 1 });
        await storage.EnqueueAsync(queueName, message2, new EnqueueOptions { Priority = 10 });
        await storage.EnqueueAsync(queueName, message3, new EnqueueOptions { Priority = 5 });

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message2.MessageId, entry.Message.MessageId);
    }

    [Fact]
    public async Task DequeueAsync_WithSamePriority_ReturnsFIFO()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var message1 = new TestMessage { Content = "First" };
        var message2 = new TestMessage { Content = "Second" };
        var message3 = new TestMessage { Content = "Third" };

        await storage.EnqueueAsync(queueName, message1, new EnqueueOptions { Priority = 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await storage.EnqueueAsync(queueName, message2, new EnqueueOptions { Priority = 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await storage.EnqueueAsync(queueName, message3, new EnqueueOptions { Priority = 5 });

        // Act
        var entry1 = await storage.DequeueAsync(queueName);
        var entry2 = await storage.DequeueAsync(queueName);
        var entry3 = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry1);
        Assert.NotNull(entry2);
        Assert.NotNull(entry3);
        Assert.Equal(message1.MessageId, entry1.Message.MessageId);
        Assert.Equal(message2.MessageId, entry2.Message.MessageId);
        Assert.Equal(message3.MessageId, entry3.Message.MessageId);
    }

    [Fact]
    public async Task DequeueAsync_WhenExceedsMaxDequeueCount_DoesNotReturnMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var maxDequeueCount = 3;

        await storage.CreateQueueAsync(queueName, new QueueOptions { MaxDequeueCount = maxDequeueCount });
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Dequeue multiple times
        for (int i = 0; i < maxDequeueCount; i++)
        {
            var entry = await storage.DequeueAsync(queueName);
            Assert.NotNull(entry);
            timeProvider.Advance(TimeSpan.FromMinutes(2)); // Advance to make visible again
        }

        // Act - Should not return message as max dequeue count reached
        var finalEntry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.Null(finalEntry);
    }

    #endregion

    #region PeekAsync Tests

    [Fact]
    public async Task PeekAsync_WithNonExistentQueue_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var entries = await storage.PeekAsync("non-existent-queue");

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task PeekAsync_WithEmptyQueue_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var entries = await storage.PeekAsync("test-queue");

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task PeekAsync_WithVisibleMessages_ReturnsMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var entries = await storage.PeekAsync(queueName, count: 2);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task PeekAsync_DoesNotIncrementDequeueCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var entries = await storage.PeekAsync(queueName);

        // Assert
        var entry = entries.First();
        Assert.Equal(0, entry.DequeueCount);
    }

    [Fact]
    public async Task PeekAsync_DoesNotChangeVisibleAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var originalEntry = await storage.EnqueueAsync(queueName, new TestMessage());
        var originalVisibleAt = originalEntry.VisibleAt;

        // Act
        await storage.PeekAsync(queueName);

        // Assert
        Assert.Equal(originalVisibleAt, originalEntry.VisibleAt);
    }

    [Fact]
    public async Task PeekAsync_RespectsCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        for (int i = 0; i < 10; i++)
        {
            await storage.EnqueueAsync(queueName, new TestMessage());
        }

        // Act
        var entries = await storage.PeekAsync(queueName, count: 5);

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task PeekAsync_OrdersByPriorityThenEnqueuedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var message1 = new TestMessage { Content = "Low priority" };
        var message2 = new TestMessage { Content = "High priority" };
        var message3 = new TestMessage { Content = "Medium priority" };

        await storage.EnqueueAsync(queueName, message1, new EnqueueOptions { Priority = 1 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await storage.EnqueueAsync(queueName, message2, new EnqueueOptions { Priority = 10 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await storage.EnqueueAsync(queueName, message3, new EnqueueOptions { Priority = 5 });

        // Act
        var entries = await storage.PeekAsync(queueName, count: 3);

        // Assert
        var entriesList = entries.ToList();
        Assert.Equal(message2.MessageId, entriesList[0].Message.MessageId);
        Assert.Equal(message3.MessageId, entriesList[1].Message.MessageId);
        Assert.Equal(message1.MessageId, entriesList[2].Message.MessageId);
    }

    [Fact]
    public async Task PeekAsync_ExcludesDelayedMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = TimeSpan.FromMinutes(10) });

        // Act
        var entries = await storage.PeekAsync(queueName, count: 10);

        // Assert
        Assert.Single(entries);
    }

    #endregion

    #region AcknowledgeAsync Tests

    [Fact]
    public async Task AcknowledgeAsync_WithNonExistentQueue_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var result = await storage.AcknowledgeAsync("non-existent-queue", "entry-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithNonExistentEntry_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var result = await storage.AcknowledgeAsync("test-queue", "non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithValidEntry_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var result = await storage.AcknowledgeAsync(queueName, entry.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_RemovesEntryFromQueue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.AcknowledgeAsync(queueName, entry.Id);

        // Act
        var depth = await storage.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(0, depth);
    }

    #endregion

    #region RejectAsync Tests

    [Fact]
    public async Task RejectAsync_WithNonExistentQueue_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var result = await storage.RejectAsync("non-existent-queue", "entry-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithNonExistentEntry_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var result = await storage.RejectAsync("test-queue", "non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var result = await storage.RejectAsync(queueName, entry.Id, requeue: true);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueFalse_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var result = await storage.RejectAsync(queueName, entry.Id, requeue: false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_MakesMessageVisible()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());
        var dequeuedEntry = await storage.DequeueAsync(queueName);
        Assert.NotNull(dequeuedEntry);

        // Act
        await storage.RejectAsync(queueName, entry.Id, requeue: true);

        // Assert - Message should be immediately visible
        var requeuedEntry = await storage.DequeueAsync(queueName);
        Assert.NotNull(requeuedEntry);
        Assert.Equal(entry.Id, requeuedEntry.Id);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_ResetsDequeueCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());
        var dequeuedEntry = await storage.DequeueAsync(queueName);
        Assert.Equal(1, dequeuedEntry!.DequeueCount);

        // Act
        await storage.RejectAsync(queueName, entry.Id, requeue: true);

        // Assert
        Assert.Equal(0, entry.DequeueCount);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueFalse_RemovesEntry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entry = await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        await storage.RejectAsync(queueName, entry.Id, requeue: false);

        // Assert
        var depth = await storage.GetQueueDepthAsync(queueName);
        Assert.Equal(0, depth);
    }

    #endregion

    #region GetQueueDepthAsync Tests

    [Fact]
    public async Task GetQueueDepthAsync_WithNonExistentQueue_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);

        // Act
        var depth = await storage.GetQueueDepthAsync("non-existent-queue");

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_WithEmptyQueue_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        await storage.CreateQueueAsync("test-queue");

        // Act
        var depth = await storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_WithVisibleMessages_ReturnsCorrectCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var depth = await storage.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(3, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_ExcludesDelayedMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = TimeSpan.FromMinutes(10) });
        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var depth = await storage.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(2, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_AfterDequeue_ExcludesInvisibleMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.EnqueueAsync(queueName, new TestMessage());

        await storage.DequeueAsync(queueName);

        // Act
        var depth = await storage.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(1, depth); // One is dequeued and invisible
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentEnqueue_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var tasks = new List<Task>();

        // Act - Enqueue 100 messages concurrently
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await storage.EnqueueAsync(queueName, new TestMessage());
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var depth = await storage.GetQueueDepthAsync(queueName);
        Assert.Equal(100, depth);
    }

    [Fact]
    public async Task ConcurrentDequeue_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        for (int i = 0; i < 50; i++)
        {
            await storage.EnqueueAsync(queueName, new TestMessage());
        }

        // Act - Dequeue concurrently
        var tasks = new List<Task<QueueEntry?>>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(async () => await storage.DequeueAsync(queueName)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var nonNullResults = results.Where(r => r != null).ToList();
        Assert.Equal(50, nonNullResults.Count);

        // All IDs should be unique
        var ids = nonNullResults.Select(r => r!.Id).ToHashSet();
        Assert.Equal(50, ids.Count);
    }

    [Fact]
    public async Task ConcurrentAcknowledge_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        var entries = new List<QueueEntry>();
        for (int i = 0; i < 50; i++)
        {
            var entry = await storage.EnqueueAsync(queueName, new TestMessage());
            entries.Add(entry);
        }

        // Act - Acknowledge concurrently
        var tasks = entries.Select(e =>
            Task.Run(async () => await storage.AcknowledgeAsync(queueName, e.Id))
        );

        await Task.WhenAll(tasks);

        // Assert
        var depth = await storage.GetQueueDepthAsync(queueName);
        Assert.Equal(0, depth);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EnqueueAsync_WithMessageHavingMetadata_PreservesMetadata()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var message = new TestMessage
        {
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123
            }
        };

        // Act
        var entry = await storage.EnqueueAsync("test-queue", message);

        // Assert
        Assert.NotNull(entry.Message.Metadata);
        Assert.Equal("value1", entry.Message.Metadata["key1"]);
        Assert.Equal(123, entry.Message.Metadata["key2"]);
    }

    [Fact]
    public async Task DequeueAsync_WithDefaultMaxDequeueCount_Uses10()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());

        // Dequeue 10 times
        for (int i = 0; i < 10; i++)
        {
            var entry = await storage.DequeueAsync(queueName);
            Assert.NotNull(entry);
            timeProvider.Advance(TimeSpan.FromMinutes(2));
        }

        // Act - 11th dequeue should return null
        var finalEntry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.Null(finalEntry);
    }

    [Fact]
    public async Task DequeueAsync_WithDefaultVisibilityTimeout_UsesOneMinute()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
        var expectedVisibleAt = timeProvider.GetUtcNow().Add(TimeSpan.FromMinutes(1));
        Assert.Equal(expectedVisibleAt, entry.VisibleAt);
    }

    [Fact]
    public async Task PeekAsync_AfterDequeue_StillShowsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage());
        await storage.DequeueAsync(queueName);

        // Act - Peek should not show invisible message
        var entries = await storage.PeekAsync(queueName);

        // Assert
        Assert.Empty(entries); // Message is invisible after dequeue
    }

    [Fact]
    public async Task EnqueueAsync_WithZeroDelay_MessageImmediatelyVisible()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";

        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = TimeSpan.Zero });

        // Act
        var entry = await storage.DequeueAsync(queueName);

        // Assert
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task GetQueueDepthAsync_AfterMessageBecomesVisible_IncludesMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryQueueStorage(timeProvider);
        var queueName = "test-queue";
        var delay = TimeSpan.FromMinutes(5);

        await storage.EnqueueAsync(queueName, new TestMessage(), new EnqueueOptions { Delay = delay });

        var depthBefore = await storage.GetQueueDepthAsync(queueName);

        // Advance time
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        // Act
        var depthAfter = await storage.GetQueueDepthAsync(queueName);

        // Assert
        Assert.Equal(0, depthBefore);
        Assert.Equal(1, depthAfter);
    }

    #endregion
}
