using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

/// <summary>
/// Unit tests for InMemoryInboxStorage
/// Tests inbox pattern implementation for message deduplication and processing tracking
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryInboxStorageTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryInboxStorage _storage;

    public InMemoryInboxStorageTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _storage = new InMemoryInboxStorage(_timeProvider);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithNewMessage_AddsSuccessfully()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var options = new InboxOptions { RequireIdempotency = false };

        // Act
        var entry = await _storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Pending, entry.Status);
        Assert.Equal(message.MessageId.ToString(), entry.Id);
    }

    [Fact]
    public async Task AddAsync_WithIdempotencyEnabled_DetectsDuplicates()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId, Content = "First" };
        var message2 = new TestMessage { MessageId = messageId, Content = "Duplicate" };
        var options = new InboxOptions { RequireIdempotency = true };

        // Act
        var first = await _storage.AddAsync(message1, options);
        var duplicate = await _storage.AddAsync(message2, options);

        // Assert
        Assert.NotNull(first);
        Assert.Null(duplicate);
    }

    [Fact]
    public async Task AddAsync_WithIdempotencyDisabled_AllowsDuplicates()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId, Content = "First" };
        var message2 = new TestMessage { MessageId = messageId, Content = "Second" };
        var options = new InboxOptions { RequireIdempotency = false };

        // Act
        var first = await _storage.AddAsync(message1, options);
        var second = await _storage.AddAsync(message2, options);

        // Assert - both should be added (second overwrites first in this implementation)
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task AddAsync_SetsReceivedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Timestamped" };
        var options = new InboxOptions();
        var expectedTime = _timeProvider.GetUtcNow().DateTime;

        // Act
        var entry = await _storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(expectedTime, entry.ReceivedAt);
    }

    #endregion

    #region IsDuplicateAsync Tests

    [Fact]
    public async Task IsDuplicateAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Act
        var isDuplicate = await _storage.IsDuplicateAsync("non-existent-id");

        // Assert
        Assert.False(isDuplicate);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Exists" };
        await _storage.AddAsync(message, new InboxOptions());

        // Act
        var isDuplicate = await _storage.IsDuplicateAsync(message.MessageId.ToString());

        // Assert
        Assert.True(isDuplicate);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithWindowOutsideRange_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old" };
        await _storage.AddAsync(message, new InboxOptions());

        // Advance time beyond window
        _timeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        var isDuplicate = await _storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            window: TimeSpan.FromHours(1));

        // Assert
        Assert.False(isDuplicate);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithWindowInsideRange_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };
        await _storage.AddAsync(message, new InboxOptions());

        // Advance time within window
        _timeProvider.Advance(TimeSpan.FromMinutes(30));

        // Act
        var isDuplicate = await _storage.IsDuplicateAsync(
            message.MessageId.ToString(),
            window: TimeSpan.FromHours(1));

        // Assert
        Assert.True(isDuplicate);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithNonExistentMessage_ReturnsNull()
    {
        // Act
        var entry = await _storage.GetAsync("non-existent-id");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAsync_WithExistingMessage_ReturnsEntry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retrievable" };
        await _storage.AddAsync(message, new InboxOptions());

        // Act
        var entry = await _storage.GetAsync(message.MessageId.ToString());

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.MessageId.ToString(), entry.Id);
        Assert.Equal("Retrievable", ((TestMessage)entry.Message).Content);
    }

    #endregion

    #region MarkProcessedAsync Tests

    [Fact]
    public async Task MarkProcessedAsync_WithExistingMessage_MarksAsProcessed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToProcess" };
        await _storage.AddAsync(message, new InboxOptions());

        // Act
        var marked = await _storage.MarkProcessedAsync(message.MessageId.ToString());

        // Assert
        Assert.True(marked);
        var entry = await _storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Processed, entry.Status);
        Assert.NotNull(entry.ProcessedAt);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Act
        var marked = await _storage.MarkProcessedAsync("non-existent-id");

        // Assert
        Assert.False(marked);
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Process" };
        await _storage.AddAsync(message, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var expectedProcessedAt = _timeProvider.GetUtcNow().DateTime;

        // Act
        await _storage.MarkProcessedAsync(message.MessageId.ToString());

        // Assert
        var entry = await _storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(expectedProcessedAt, entry.ProcessedAt);
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_WithExistingMessage_MarksAsFailed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToFail" };
        await _storage.AddAsync(message, new InboxOptions());

        // Act
        var marked = await _storage.MarkFailedAsync(
            message.MessageId.ToString(),
            "Test error");

        // Assert
        Assert.True(marked);
        var entry = await _storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("Test error", entry.Error);
    }

    [Fact]
    public async Task MarkFailedAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Act
        var marked = await _storage.MarkFailedAsync("non-existent-id", "Error");

        // Assert
        Assert.False(marked);
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusAndError()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Fail" };
        await _storage.AddAsync(message, new InboxOptions());

        var errorMessage = "Processing error";

        // Act
        var result = await _storage.MarkFailedAsync(message.MessageId.ToString(), errorMessage);

        // Assert
        Assert.True(result);
        var entry = await _storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal(errorMessage, entry.Error);
    }

    #endregion

    #region GetUnprocessedAsync Tests

    [Fact]
    public async Task GetUnprocessedAsync_ReturnsAllPendingEntries()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new InboxOptions());
        }

        // Act
        var entries = await _storage.GetUnprocessedAsync();

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task GetUnprocessedAsync_ExcludesProcessedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToProcess" };
        await _storage.AddAsync(message1, new InboxOptions());
        await _storage.AddAsync(message2, new InboxOptions());
        await _storage.MarkProcessedAsync(message2.MessageId.ToString());

        // Act
        var unprocessed = await _storage.GetUnprocessedAsync();

        // Assert
        Assert.Single(unprocessed);
    }

    [Fact]
    public async Task GetUnprocessedCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new InboxOptions());
        }

        // Act
        var count = await _storage.GetUnprocessedCountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    #endregion

    #region CleanupOldEntriesAsync Tests

    [Fact]
    public async Task CleanupOldEntriesAsync_RemovesOldProcessedEntries()
    {
        // Arrange
        var oldMessage = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old" };
        var recentMessage = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };

        await _storage.AddAsync(oldMessage, new InboxOptions());
        await _storage.MarkProcessedAsync(oldMessage.MessageId.ToString());

        _timeProvider.Advance(TimeSpan.FromDays(8));

        await _storage.AddAsync(recentMessage, new InboxOptions());
        await _storage.MarkProcessedAsync(recentMessage.MessageId.ToString());

        // Act
        await _storage.CleanupOldEntriesAsync(TimeSpan.FromDays(7));

        // Assert
        var oldEntry = await _storage.GetAsync(oldMessage.MessageId.ToString());
        var recentEntry = await _storage.GetAsync(recentMessage.MessageId.ToString());
        Assert.Null(oldEntry);
        Assert.NotNull(recentEntry);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_WithNoOldEntries_DoesNothing()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };
        await _storage.AddAsync(message, new InboxOptions());
        await _storage.MarkProcessedAsync(message.MessageId.ToString());

        // Act
        await _storage.CleanupOldEntriesAsync(TimeSpan.FromDays(7));

        // Assert
        var entry = await _storage.GetAsync(message.MessageId.ToString());
        Assert.NotNull(entry);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
