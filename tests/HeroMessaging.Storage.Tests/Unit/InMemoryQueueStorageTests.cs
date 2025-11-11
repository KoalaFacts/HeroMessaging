using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

/// <summary>
/// Unit tests for InMemoryQueueStorage
/// Tests queue operations, message visibility, priority handling, and lifecycle
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryQueueStorageTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryQueueStorage _storage;

    public InMemoryQueueStorageTests()
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _storage = new InMemoryQueueStorage(_timeProvider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryQueueStorage(null!));
    }

    [Fact]
    public void Constructor_WithValidTimeProvider_CreatesStorage()
    {
        // Act
        var storage = new InMemoryQueueStorage(_timeProvider);

        // Assert
        Assert.NotNull(storage);
    }

    #endregion

    #region CreateQueueAsync Tests

    [Fact]
    public async Task CreateQueueAsync_CreatesNewQueue()
    {
        // Act
        var result = await _storage.CreateQueueAsync("test-queue");

        // Assert
        Assert.True(result);
        var exists = await _storage.QueueExistsAsync("test-queue");
        Assert.True(exists);
    }

    [Fact]
    public async Task CreateQueueAsync_WithOptions_StoresOptions()
    {
        // Arrange
        var options = new QueueOptions
        {
            MaxDequeueCount = 5,
            VisibilityTimeout = TimeSpan.FromMinutes(2)
        };

        // Act
        var result = await _storage.CreateQueueAsync("test-queue", options);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateQueueAsync_DuplicateQueue_ReturnsFalse()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var result = await _storage.CreateQueueAsync("test-queue");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region EnqueueAsync Tests

    [Fact]
    public async Task EnqueueAsync_AddsMessageToQueue()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };

        // Act
        var entry = await _storage.EnqueueAsync("test-queue", message);

        // Assert
        Assert.NotNull(entry);
        Assert.NotNull(entry.Id);
        Assert.Equal(message, entry.Message);
        Assert.Equal(_timeProvider.GetUtcNow(), entry.EnqueuedAt);
    }

    [Fact]
    public async Task EnqueueAsync_WithDelay_SetsVisibleAt()
    {
        // Arrange
        var message = new TestMessage { Content = "delayed" };
        var options = new EnqueueOptions { Delay = TimeSpan.FromMinutes(5) };

        // Act
        var entry = await _storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.Equal(_timeProvider.GetUtcNow().AddMinutes(5), entry.VisibleAt);
    }

    [Fact]
    public async Task EnqueueAsync_WithPriority_StoresPriority()
    {
        // Arrange
        var message = new TestMessage { Content = "priority" };
        var options = new EnqueueOptions { Priority = 10 };

        // Act
        var entry = await _storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.Equal(10, entry.Options.Priority);
    }

    [Fact]
    public async Task EnqueueAsync_WithoutOptions_UsesDefaults()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };

        // Act
        var entry = await _storage.EnqueueAsync("test-queue", message);

        // Assert
        Assert.NotNull(entry.Options);
        Assert.Equal(_timeProvider.GetUtcNow(), entry.VisibleAt);
    }

    #endregion

    #region DequeueAsync Tests

    [Fact]
    public async Task DequeueAsync_FromNonExistentQueue_ReturnsNull()
    {
        // Act
        var entry = await _storage.DequeueAsync("non-existent");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task DequeueAsync_FromEmptyQueue_ReturnsNull()
    {
        // Arrange
        await _storage.CreateQueueAsync("empty-queue");

        // Act
        var entry = await _storage.DequeueAsync("empty-queue");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsOldestMessage()
    {
        // Arrange
        var message1 = new TestMessage { Content = "first" };
        var message2 = new TestMessage { Content = "second" };

        await _storage.EnqueueAsync("test-queue", message1);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await _storage.EnqueueAsync("test-queue", message2);

        // Act
        var entry = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("first", ((TestMessage)entry.Message).Content);
    }

    [Fact]
    public async Task DequeueAsync_WithHigherPriority_ReturnsHighPriorityFirst()
    {
        // Arrange
        var lowPriority = new TestMessage { Content = "low" };
        var highPriority = new TestMessage { Content = "high" };

        await _storage.EnqueueAsync("test-queue", lowPriority, new EnqueueOptions { Priority = 1 });
        await _storage.EnqueueAsync("test-queue", highPriority, new EnqueueOptions { Priority = 10 });

        // Act
        var entry = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("high", ((TestMessage)entry.Message).Content);
    }

    [Fact]
    public async Task DequeueAsync_IncrementsDequeueCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        await _storage.EnqueueAsync("test-queue", message);

        // Act
        var entry = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(1, entry.DequeueCount);
    }

    [Fact]
    public async Task DequeueAsync_SetsVisibilityTimeout()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        await _storage.CreateQueueAsync("test-queue", new QueueOptions { VisibilityTimeout = TimeSpan.FromMinutes(2) });
        await _storage.EnqueueAsync("test-queue", message);

        // Act
        var entry = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(_timeProvider.GetUtcNow().AddMinutes(2), entry.VisibleAt);
    }

    [Fact]
    public async Task DequeueAsync_SkipsDelayedMessages()
    {
        // Arrange
        var delayedMessage = new TestMessage { Content = "delayed" };
        var immediateMessage = new TestMessage { Content = "immediate" };

        await _storage.EnqueueAsync("test-queue", delayedMessage, new EnqueueOptions { Delay = TimeSpan.FromMinutes(10) });
        await _storage.EnqueueAsync("test-queue", immediateMessage);

        // Act
        var entry = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("immediate", ((TestMessage)entry.Message).Content);
    }

    [Fact]
    public async Task DequeueAsync_ExceedsMaxDequeueCount_SkipsMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        await _storage.CreateQueueAsync("test-queue", new QueueOptions { MaxDequeueCount = 2 });
        await _storage.EnqueueAsync("test-queue", message);

        // Act - Dequeue 3 times
        var entry1 = await _storage.DequeueAsync("test-queue");
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        var entry2 = await _storage.DequeueAsync("test-queue");
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        var entry3 = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry1);
        Assert.NotNull(entry2);
        Assert.Null(entry3); // Exceeded max dequeue count
    }

    #endregion

    #region PeekAsync Tests

    [Fact]
    public async Task PeekAsync_FromNonExistentQueue_ReturnsEmpty()
    {
        // Act
        var entries = await _storage.PeekAsync("non-existent");

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task PeekAsync_ReturnsMessagesWithoutDequeuing()
    {
        // Arrange
        var message1 = new TestMessage { Content = "first" };
        var message2 = new TestMessage { Content = "second" };

        await _storage.EnqueueAsync("test-queue", message1);
        await _storage.EnqueueAsync("test-queue", message2);

        // Act
        var entries1 = await _storage.PeekAsync("test-queue", 2);
        var entries2 = await _storage.PeekAsync("test-queue", 2);

        // Assert - Same messages returned both times
        Assert.Equal(2, entries1.Count());
        Assert.Equal(2, entries2.Count());
    }

    [Fact]
    public async Task PeekAsync_RespectsCount()
    {
        // Arrange
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "1" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "2" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "3" });

        // Act
        var entries = await _storage.PeekAsync("test-queue", 2);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task PeekAsync_SkipsInvisibleMessages()
    {
        // Arrange
        var visibleMessage = new TestMessage { Content = "visible" };
        var invisibleMessage = new TestMessage { Content = "invisible" };

        await _storage.EnqueueAsync("test-queue", invisibleMessage, new EnqueueOptions { Delay = TimeSpan.FromMinutes(10) });
        await _storage.EnqueueAsync("test-queue", visibleMessage);

        // Act
        var entries = await _storage.PeekAsync("test-queue", 10);

        // Assert
        Assert.Single(entries);
        Assert.Equal("visible", ((TestMessage)entries.First().Message).Content);
    }

    #endregion

    #region AcknowledgeAsync Tests

    [Fact]
    public async Task AcknowledgeAsync_RemovesMessageFromQueue()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var entry = await _storage.EnqueueAsync("test-queue", message);

        // Act
        var result = await _storage.AcknowledgeAsync("test-queue", entry.Id);

        // Assert
        Assert.True(result);

        // Verify message is removed
        var dequeued = await _storage.DequeueAsync("test-queue");
        Assert.Null(dequeued);
    }

    [Fact]
    public async Task AcknowledgeAsync_NonExistentQueue_ReturnsFalse()
    {
        // Act
        var result = await _storage.AcknowledgeAsync("non-existent", "some-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_NonExistentEntry_ReturnsFalse()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var result = await _storage.AcknowledgeAsync("test-queue", "non-existent-id");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RejectAsync Tests

    [Fact]
    public async Task RejectAsync_WithoutRequeue_RemovesMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var entry = await _storage.EnqueueAsync("test-queue", message);

        // Act
        var result = await _storage.RejectAsync("test-queue", entry.Id, requeue: false);

        // Assert
        Assert.True(result);

        // Verify message is removed
        var dequeued = await _storage.DequeueAsync("test-queue");
        Assert.Null(dequeued);
    }

    [Fact]
    public async Task RejectAsync_WithRequeue_ResetsVisibilityAndCount()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var entry = await _storage.EnqueueAsync("test-queue", message);
        await _storage.DequeueAsync("test-queue"); // Dequeue once

        // Act
        var result = await _storage.RejectAsync("test-queue", entry.Id, requeue: true);

        // Assert
        Assert.True(result);

        // Verify message is requeued
        var dequeued = await _storage.DequeueAsync("test-queue");
        Assert.NotNull(dequeued);
        Assert.Equal(1, dequeued.DequeueCount); // Reset to 0, then incremented by DequeueAsync
    }

    [Fact]
    public async Task RejectAsync_NonExistentQueue_ReturnsFalse()
    {
        // Act
        var result = await _storage.RejectAsync("non-existent", "some-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_NonExistentEntry_ReturnsFalse()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var result = await _storage.RejectAsync("test-queue", "non-existent-id");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetQueueDepthAsync Tests

    [Fact]
    public async Task GetQueueDepthAsync_NonExistentQueue_ReturnsZero()
    {
        // Act
        var depth = await _storage.GetQueueDepthAsync("non-existent");

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_EmptyQueue_ReturnsZero()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var depth = await _storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.Equal(0, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_ReturnsVisibleMessageCount()
    {
        // Arrange
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "1" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "2" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "3" });

        // Act
        var depth = await _storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.Equal(3, depth);
    }

    [Fact]
    public async Task GetQueueDepthAsync_ExcludesInvisibleMessages()
    {
        // Arrange
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "visible" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "delayed" },
            new EnqueueOptions { Delay = TimeSpan.FromMinutes(10) });

        // Act
        var depth = await _storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.Equal(1, depth);
    }

    #endregion

    #region DeleteQueueAsync Tests

    [Fact]
    public async Task DeleteQueueAsync_RemovesQueue()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var result = await _storage.DeleteQueueAsync("test-queue");

        // Assert
        Assert.True(result);

        var exists = await _storage.QueueExistsAsync("test-queue");
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteQueueAsync_NonExistentQueue_ReturnsFalse()
    {
        // Act
        var result = await _storage.DeleteQueueAsync("non-existent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetQueuesAsync Tests

    [Fact]
    public async Task GetQueuesAsync_ReturnsAllQueueNames()
    {
        // Arrange
        await _storage.CreateQueueAsync("queue1");
        await _storage.CreateQueueAsync("queue2");
        await _storage.CreateQueueAsync("queue3");

        // Act
        var queues = await _storage.GetQueuesAsync();

        // Assert
        Assert.Equal(3, queues.Count());
        Assert.Contains("queue1", queues);
        Assert.Contains("queue2", queues);
        Assert.Contains("queue3", queues);
    }

    [Fact]
    public async Task GetQueuesAsync_NoQueues_ReturnsEmpty()
    {
        // Act
        var queues = await _storage.GetQueuesAsync();

        // Assert
        Assert.Empty(queues);
    }

    #endregion

    #region QueueExistsAsync Tests

    [Fact]
    public async Task QueueExistsAsync_ExistingQueue_ReturnsTrue()
    {
        // Arrange
        await _storage.CreateQueueAsync("test-queue");

        // Act
        var exists = await _storage.QueueExistsAsync("test-queue");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task QueueExistsAsync_NonExistentQueue_ReturnsFalse()
    {
        // Act
        var exists = await _storage.QueueExistsAsync("non-existent");

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task EnqueueAsync_CreatesQueueImplicitlyIfNotExists()
    {
        // Arrange
        var message = new TestMessage { Content = "auto-created" };

        // Act - Enqueue without creating queue first
        var entry = await _storage.EnqueueAsync("auto-queue", message);

        // Assert
        Assert.NotNull(entry);
        var exists = await _storage.QueueExistsAsync("auto-queue");
        Assert.True(exists);
    }

    [Fact]
    public async Task DequeueAsync_WithMaxDequeueCountExactlyReached_SkipsOnNextDequeue()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        await _storage.CreateQueueAsync("test-queue", new QueueOptions { MaxDequeueCount = 1 });
        await _storage.EnqueueAsync("test-queue", message);

        // Act - Dequeue once (hits the max)
        var entry1 = await _storage.DequeueAsync("test-queue");
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var entry2 = await _storage.DequeueAsync("test-queue");

        // Assert
        Assert.NotNull(entry1);
        Assert.Equal(1, entry1.DequeueCount);
        Assert.Null(entry2); // Message should be skipped because MaxDequeueCount = 1
    }

    [Fact]
    public async Task PeekAsync_WithZeroCount_ReturnsEmpty()
    {
        // Arrange
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "1" });
        await _storage.EnqueueAsync("test-queue", new TestMessage { Content = "2" });

        // Act
        var entries = await _storage.PeekAsync("test-queue", 0);

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetQueueDepthAsync_IncludesDequeuedMessagesThatAreInvisible()
    {
        // Arrange
        var message = new TestMessage { Content = "dequeued" };
        await _storage.CreateQueueAsync("test-queue", new QueueOptions { VisibilityTimeout = TimeSpan.FromMinutes(5) });
        var entry = await _storage.EnqueueAsync("test-queue", message);

        // Act - Dequeue the message (makes it invisible)
        await _storage.DequeueAsync("test-queue");
        var depthAfterDequeue = await _storage.GetQueueDepthAsync("test-queue");

        // Advance time beyond visibility
        _timeProvider.Advance(TimeSpan.FromMinutes(6));
        var depthAfterVisibility = await _storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.Equal(0, depthAfterDequeue); // Message is invisible immediately after dequeue
        Assert.Equal(1, depthAfterVisibility); // Message becomes visible again
    }

    [Fact]
    public async Task RejectAsync_WithRequeue_MakesMessageVisibleImmediately()
    {
        // Arrange
        var message = new TestMessage { Content = "requeue-test" };
        await _storage.EnqueueAsync("test-queue", message);
        var dequeuedEntry = await _storage.DequeueAsync("test-queue");

        // Assert message is invisible after dequeue
        var depthWhileInvisible = await _storage.GetQueueDepthAsync("test-queue");
        Assert.Equal(0, depthWhileInvisible);

        // Act - Reject with requeue
        var rejectResult = await _storage.RejectAsync("test-queue", dequeuedEntry!.Id, requeue: true);

        // Assert
        Assert.True(rejectResult);
        var depthAfterRequeue = await _storage.GetQueueDepthAsync("test-queue");
        Assert.Equal(1, depthAfterRequeue); // Message is immediately visible
        var reequeuedEntry = await _storage.DequeueAsync("test-queue");
        Assert.NotNull(reequeuedEntry);
        Assert.Equal(dequeuedEntry.Id, reequeuedEntry.Id);
        Assert.Equal(1, reequeuedEntry.DequeueCount); // Incremented again during dequeue
    }

    [Fact]
    public async Task MultipleQueuesAreIndependent()
    {
        // Arrange & Act
        await _storage.EnqueueAsync("queue-a", new TestMessage { Content = "a1" });
        await _storage.EnqueueAsync("queue-a", new TestMessage { Content = "a2" });
        await _storage.EnqueueAsync("queue-b", new TestMessage { Content = "b1" });

        // Assert
        var depthA = await _storage.GetQueueDepthAsync("queue-a");
        var depthB = await _storage.GetQueueDepthAsync("queue-b");
        Assert.Equal(2, depthA);
        Assert.Equal(1, depthB);

        // Dequeue from queue-a should not affect queue-b
        var entryA = await _storage.DequeueAsync("queue-a");
        Assert.NotNull(entryA);

        var depthAAfter = await _storage.GetQueueDepthAsync("queue-a");
        var depthBAfter = await _storage.GetQueueDepthAsync("queue-b");
        Assert.Equal(1, depthAAfter);
        Assert.Equal(1, depthBAfter);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
