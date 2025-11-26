using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public sealed class InMemoryInboxStorageTests
{
    #region Test Helper Classes

    public sealed class TestMessage : IMessage
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
        var storage = new InMemoryInboxStorage(timeProvider);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InMemoryInboxStorage(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithValidMessage_ReturnsInboxEntry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var options = new InboxOptions { Source = "TestSource" };

        // Act
        var entry = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.MessageId.ToString(), entry.Id);
        Assert.Same(message, entry.Message);
        Assert.Equal(InboxStatus.Pending, entry.Status);
        Assert.Equal(timeProvider.GetUtcNow(), entry.ReceivedAt);
        Assert.Null(entry.ProcessedAt);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task AddAsync_WithRequireIdempotencyTrue_StoresDuplicateAsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId, Content = "First" };
        var message2 = new TestMessage { MessageId = messageId, Content = "Second" };
        var options = new InboxOptions { RequireIdempotency = true };

        // Act
        var entry1 = await storage.AddAsync(message1, options);
        var entry2 = await storage.AddAsync(message2, options);

        // Assert
        Assert.NotNull(entry1);
        Assert.Null(entry2);

        // Verify the stored entry is marked as duplicate
        var stored = await storage.GetAsync(messageId.ToString());
        Assert.NotNull(stored);
        Assert.Equal(InboxStatus.Duplicate, stored.Status);
    }

    [Fact]
    public async Task AddAsync_WithRequireIdempotencyFalse_AllowsDuplicates()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId, Content = "First" };
        var message2 = new TestMessage { MessageId = messageId, Content = "Second" };
        var options = new InboxOptions { RequireIdempotency = false };

        // Act
        var entry1 = await storage.AddAsync(message1, options);
        var entry2 = await storage.AddAsync(message2, options);

        // Assert
        Assert.NotNull(entry1);
        Assert.NotNull(entry2);
        Assert.Same(message2, entry2.Message);
    }

    [Fact]
    public async Task AddAsync_StoresOptions()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        var options = new InboxOptions
        {
            Source = "TestSource",
            RequireIdempotency = true,
            DeduplicationWindow = TimeSpan.FromMinutes(5)
        };

        // Act
        var entry = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.Same(options, entry.Options);
    }

    #endregion

    #region IsDuplicateAsync Tests

    [Fact]
    public async Task IsDuplicateAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act
        var isDuplicate = await storage.IsDuplicateAsync("non-existent-id");

        // Assert
        Assert.False(isDuplicate);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Act
        var isDuplicate = await storage.IsDuplicateAsync(message.MessageId.ToString());

        // Assert
        Assert.True(isDuplicate);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithWindow_ChecksTimeWindow()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Act - Within window
        var isDuplicateWithin = await storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            TimeSpan.FromMinutes(5));

        // Advance time beyond window
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        var isDuplicateBeyond = await storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            TimeSpan.FromMinutes(5));

        // Assert
        Assert.True(isDuplicateWithin);
        Assert.False(isDuplicateBeyond);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithWindowAtBoundary_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Advance exactly to window boundary
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var isDuplicate = await storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(isDuplicate);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithNonExistentMessage_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act
        var entry = await storage.GetAsync("non-existent-id");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAsync_WithExistingMessage_ReturnsEntry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        await storage.AddAsync(message, new InboxOptions());

        // Act
        var entry = await storage.GetAsync(message.MessageId.ToString());

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.MessageId.ToString(), entry.Id);
        Assert.Same(message, entry.Message);
        Assert.Equal(InboxStatus.Pending, entry.Status);
    }

    #endregion

    #region MarkProcessedAsync Tests

    [Fact]
    public async Task MarkProcessedAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Act
        var result = await storage.MarkProcessedAsync(message.MessageId.ToString());

        // Assert
        Assert.True(result);

        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Processed, entry.Status);
        Assert.Equal(timeProvider.GetUtcNow(), entry.ProcessedAt);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act
        var result = await storage.MarkProcessedAsync("non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedAtTimestamp()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var expectedProcessedAt = timeProvider.GetUtcNow();

        // Act
        await storage.MarkProcessedAsync(message.MessageId.ToString());

        // Assert
        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(expectedProcessedAt, entry.ProcessedAt);
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Act
        var result = await storage.MarkFailedAsync(message.MessageId.ToString(), "Test error");

        // Assert
        Assert.True(result);

        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("Test error", entry.Error);
    }

    [Fact]
    public async Task MarkFailedAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act
        var result = await storage.MarkFailedAsync("non-existent-id", "Error");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkFailedAsync_StoresErrorMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());
        var errorMessage = "Processing failed: Invalid data format";

        // Act
        await storage.MarkFailedAsync(message.MessageId.ToString(), errorMessage);

        // Assert
        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(errorMessage, entry.Error);
    }

    #endregion

    #region GetPendingAsync Tests

    [Fact]
    public async Task GetPendingAsync_WithQuery_ReturnsOnlyPendingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var message3 = new TestMessage();

        await storage.AddAsync(message1, new InboxOptions());
        await storage.AddAsync(message2, new InboxOptions());
        await storage.AddAsync(message3, new InboxOptions());

        await storage.MarkProcessedAsync(message2.MessageId.ToString());

        var query = new InboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        Assert.Contains(resultsList, e => e.Id == message1.MessageId.ToString());
        Assert.Contains(resultsList, e => e.Id == message3.MessageId.ToString());
    }

    [Fact]
    public async Task GetPendingAsync_WithStatusFilter_ReturnsMatchingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var message3 = new TestMessage();

        await storage.AddAsync(message1, new InboxOptions());
        await storage.AddAsync(message2, new InboxOptions());
        await storage.AddAsync(message3, new InboxOptions());

        await storage.MarkProcessedAsync(message2.MessageId.ToString());
        await storage.MarkFailedAsync(message3.MessageId.ToString(), "Error");

        var query = new InboxQuery { Status = InboxEntryStatus.Failed };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(message3.MessageId.ToString(), resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithOlderThanFilter_ReturnsCorrectEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        await storage.AddAsync(message1, new InboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var message2 = new TestMessage();
        await storage.AddAsync(message2, new InboxOptions());

        var query = new InboxQuery { OlderThan = cutoffTime };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(message1.MessageId.ToString(), resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithNewerThanFilter_ReturnsCorrectEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        await storage.AddAsync(message1, new InboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var message2 = new TestMessage();
        await storage.AddAsync(message2, new InboxOptions());

        var query = new InboxQuery { NewerThan = cutoffTime };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(message2.MessageId.ToString(), resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_RespectsLimit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.AddAsync(new TestMessage(), new InboxOptions());
        }

        var query = new InboxQuery { Limit = 5 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByReceivedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage();
            messages.Add(message);
            await storage.AddAsync(message, new InboxOptions());
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new InboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId.ToString(), resultsList[i].Id);
        }
    }

    [Fact]
    public async Task GetPendingAsync_WithEmptyStorage_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var query = new InboxQuery();

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GetUnprocessedAsync Tests

    [Fact]
    public async Task GetUnprocessedAsync_ReturnsOnlyPendingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var message3 = new TestMessage();

        await storage.AddAsync(message1, new InboxOptions());
        await storage.AddAsync(message2, new InboxOptions());
        await storage.AddAsync(message3, new InboxOptions());

        await storage.MarkProcessedAsync(message2.MessageId.ToString());

        // Act
        var results = await storage.GetUnprocessedAsync();

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        Assert.All(resultsList, e => Assert.Equal(InboxStatus.Pending, e.Status));
    }

    [Fact]
    public async Task GetUnprocessedAsync_RespectsLimit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.AddAsync(new TestMessage(), new InboxOptions());
        }

        // Act
        var results = await storage.GetUnprocessedAsync(limit: 5);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task GetUnprocessedAsync_OrdersByReceivedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage();
            messages.Add(message);
            await storage.AddAsync(message, new InboxOptions());
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var results = await storage.GetUnprocessedAsync();

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId.ToString(), resultsList[i].Id);
        }
    }

    #endregion

    #region GetUnprocessedCountAsync Tests

    [Fact]
    public async Task GetUnprocessedCountAsync_WithNoPendingMessages_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act
        var count = await storage.GetUnprocessedCountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetUnprocessedCountAsync_WithPendingMessages_ReturnsCorrectCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        await storage.AddAsync(new TestMessage(), new InboxOptions());
        await storage.AddAsync(new TestMessage(), new InboxOptions());
        await storage.AddAsync(new TestMessage(), new InboxOptions());

        // Act
        var count = await storage.GetUnprocessedCountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetUnprocessedCountAsync_ExcludesProcessedMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var message3 = new TestMessage();

        await storage.AddAsync(message1, new InboxOptions());
        await storage.AddAsync(message2, new InboxOptions());
        await storage.AddAsync(message3, new InboxOptions());

        await storage.MarkProcessedAsync(message2.MessageId.ToString());

        // Act
        var count = await storage.GetUnprocessedCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region CleanupOldEntriesAsync Tests

    [Fact]
    public async Task CleanupOldEntriesAsync_RemovesOldProcessedEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message1 = new TestMessage();
        await storage.AddAsync(message1, new InboxOptions());
        await storage.MarkProcessedAsync(message1.MessageId.ToString());

        timeProvider.Advance(TimeSpan.FromHours(2));

        var message2 = new TestMessage();
        await storage.AddAsync(message2, new InboxOptions());
        await storage.MarkProcessedAsync(message2.MessageId.ToString());

        // Act
        await storage.CleanupOldEntriesAsync(TimeSpan.FromHours(1));

        // Assert
        var entry1 = await storage.GetAsync(message1.MessageId.ToString());
        var entry2 = await storage.GetAsync(message2.MessageId.ToString());

        Assert.Null(entry1);
        Assert.NotNull(entry2);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_DoesNotRemovePendingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        timeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        await storage.CleanupOldEntriesAsync(TimeSpan.FromHours(1));

        // Assert
        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_DoesNotRemoveFailedEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());
        await storage.MarkFailedAsync(message.MessageId.ToString(), "Error");

        timeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        await storage.CleanupOldEntriesAsync(TimeSpan.FromHours(1));

        // Assert
        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_WithEmptyStorage_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        // Act & Assert
        await storage.CleanupOldEntriesAsync(TimeSpan.FromHours(1));
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentAddAndGet_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var tasks = new List<Task>();

        // Act - Add 100 messages concurrently
        var messages = new List<TestMessage>();
        for (int i = 0; i < 100; i++)
        {
            var message = new TestMessage { Content = $"Message {i}" };
            messages.Add(message);
            tasks.Add(Task.Run(async () =>
            {
                await storage.AddAsync(message, new InboxOptions());
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - All messages should be retrievable
        foreach (var message in messages)
        {
            var entry = await storage.GetAsync(message.MessageId.ToString());
            Assert.NotNull(entry);
            Assert.Equal(message.MessageId.ToString(), entry.Id);
        }
    }

    [Fact]
    public async Task ConcurrentMarkProcessed_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 50; i++)
        {
            var message = new TestMessage();
            messages.Add(message);
            await storage.AddAsync(message, new InboxOptions());
        }

        // Act - Mark processed concurrently
        var tasks = messages.Select(m =>
            Task.Run(async () => await storage.MarkProcessedAsync(m.MessageId.ToString()))
        );

        await Task.WhenAll(tasks);

        // Assert
        var count = await storage.GetUnprocessedCountAsync();
        Assert.Equal(0, count);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task AddAsync_WithMessageHavingMetadata_StoresCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage
        {
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123
            }
        };

        // Act
        await storage.AddAsync(message, new InboxOptions());

        // Assert
        var entry = await storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.NotNull(entry.Message.Metadata);
        Assert.Equal("value1", entry.Message.Metadata["key1"]);
        Assert.Equal(123, entry.Message.Metadata["key2"]);
    }

    [Fact]
    public async Task GetPendingAsync_WithAllEntriesProcessed_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);

        var messages = new[] { new TestMessage(), new TestMessage(), new TestMessage() };
        foreach (var message in messages)
        {
            await storage.AddAsync(message, new InboxOptions());
            await storage.MarkProcessedAsync(message.MessageId.ToString());
        }

        var query = new InboxQuery();

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithVeryShortWindow_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryInboxStorage(timeProvider);
        var message = new TestMessage();
        await storage.AddAsync(message, new InboxOptions());

        // Act - Check with 1 millisecond window
        var isDuplicate1 = await storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            TimeSpan.FromMilliseconds(1));

        timeProvider.Advance(TimeSpan.FromMilliseconds(2));

        var isDuplicate2 = await storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.True(isDuplicate1);
        Assert.False(isDuplicate2);
    }

    #endregion
}
