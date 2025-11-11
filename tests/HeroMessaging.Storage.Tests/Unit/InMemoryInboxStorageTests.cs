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
        var expectedTime = _timeProvider.GetUtcNow();

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
        var expectedProcessedAt = _timeProvider.GetUtcNow();

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

    #region GetPendingAsync Tests

    [Fact]
    public async Task GetPendingAsync_WithDefaultQuery_ReturnsPendingEntries()
    {
        // Arrange
        var pending1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending1" };
        var pending2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending2" };
        var processed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };

        await _storage.AddAsync(pending1, new InboxOptions());
        await _storage.AddAsync(pending2, new InboxOptions());
        await _storage.AddAsync(processed, new InboxOptions());
        await _storage.MarkProcessedAsync(processed.MessageId.ToString());

        var query = new InboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithStatusFilter_ReturnsMatchingEntries()
    {
        // Arrange
        var pending = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var processed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var failed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        await _storage.AddAsync(pending, new InboxOptions());
        await _storage.AddAsync(processed, new InboxOptions());
        await _storage.AddAsync(failed, new InboxOptions());
        await _storage.MarkProcessedAsync(processed.MessageId.ToString());
        await _storage.MarkFailedAsync(failed.MessageId.ToString(), "Test error");

        var query = new InboxQuery
        {
            Status = InboxEntryStatus.Processed,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(InboxStatus.Processed, entries.First().Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithOlderThanFilter_ReturnsOldEntries()
    {
        // Arrange
        var old1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old1" };
        var old2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old2" };
        await _storage.AddAsync(old1, new InboxOptions());
        await _storage.AddAsync(old2, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var recent = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };
        await _storage.AddAsync(recent, new InboxOptions());

        var cutoff = _timeProvider.GetUtcNow().AddHours(-1);
        var query = new InboxQuery
        {
            OlderThan = cutoff,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithNewerThanFilter_ReturnsRecentEntries()
    {
        // Arrange
        var old = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old" };
        await _storage.AddAsync(old, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var recent1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent1" };
        var recent2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent2" };
        await _storage.AddAsync(recent1, new InboxOptions());
        await _storage.AddAsync(recent2, new InboxOptions());

        var cutoff = _timeProvider.GetUtcNow().AddHours(-1);
        var query = new InboxQuery
        {
            NewerThan = cutoff,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithLimit_ReturnsLimitedEntries()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new InboxOptions());
        }

        var query = new InboxQuery { Limit = 5 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByReceivedAt()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "First" };
        await _storage.AddAsync(message1, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Second" };
        await _storage.AddAsync(message2, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Third" };
        await _storage.AddAsync(message3, new InboxOptions());

        var query = new InboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        var entryList = entries.ToList();
        Assert.Equal("First", ((TestMessage)entryList[0].Message).Content);
        Assert.Equal("Second", ((TestMessage)entryList[1].Message).Content);
        Assert.Equal("Third", ((TestMessage)entryList[2].Message).Content);
    }

    [Fact]
    public async Task GetPendingAsync_WithAllStatusFilters_ReturnsCorrectEntries()
    {
        // Arrange - Create entries with different statuses
        var pending = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var processed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var failed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        await _storage.AddAsync(pending, new InboxOptions());
        await _storage.AddAsync(processed, new InboxOptions());
        await _storage.AddAsync(failed, new InboxOptions());
        await _storage.MarkProcessedAsync(processed.MessageId.ToString());
        await _storage.MarkFailedAsync(failed.MessageId.ToString(), "Error");

        // Act & Assert - Test each status
        var pendingQuery = new InboxQuery { Status = InboxEntryStatus.Pending, Limit = 10 };
        var pendingEntries = await _storage.GetPendingAsync(pendingQuery);
        Assert.Single(pendingEntries);
        Assert.Equal(InboxStatus.Pending, pendingEntries.First().Status);

        var processedQuery = new InboxQuery { Status = InboxEntryStatus.Processed, Limit = 10 };
        var processedEntries = await _storage.GetPendingAsync(processedQuery);
        Assert.Single(processedEntries);
        Assert.Equal(InboxStatus.Processed, processedEntries.First().Status);

        var failedQuery = new InboxQuery { Status = InboxEntryStatus.Failed, Limit = 10 };
        var failedEntries = await _storage.GetPendingAsync(failedQuery);
        Assert.Single(failedEntries);
        Assert.Equal(InboxStatus.Failed, failedEntries.First().Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithDuplicateEntry_FiltersByStatus()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId, Content = "First" };
        var message2 = new TestMessage { MessageId = messageId, Content = "Duplicate" };
        var options = new InboxOptions { RequireIdempotency = true };

        await _storage.AddAsync(message1, options);
        await _storage.AddAsync(message2, options); // Creates duplicate

        var query = new InboxQuery { Status = InboxEntryStatus.Duplicate, Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(InboxStatus.Duplicate, entries.First().Status);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryInboxStorage(null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Edge Case Tests for Coverage

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingAsync_WithOlderThanAndNewerThanCombined_FiltersCorrectly()
    {
        // Arrange - Create messages at different times
        var veryOld = new TestMessage { MessageId = Guid.NewGuid(), Content = "VeryOld" };
        await _storage.AddAsync(veryOld, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(1));
        var middle = new TestMessage { MessageId = Guid.NewGuid(), Content = "Middle" };
        await _storage.AddAsync(middle, new InboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(1));
        var recent = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };
        await _storage.AddAsync(recent, new InboxOptions());

        // Define time window boundaries
        var olderThanCutoff = _timeProvider.GetUtcNow().AddHours(-0.5); // Excludes recent
        var newerThanCutoff = _timeProvider.GetUtcNow().AddHours(-2); // Includes middle and recent

        var query = new InboxQuery
        {
            OlderThan = olderThanCutoff,
            NewerThan = newerThanCutoff,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert - Should return only middle (between the two cutoffs)
        Assert.Single(entries);
        Assert.Equal("Middle", ((TestMessage)entries.First().Message).Content);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CleanupOldEntriesAsync_WithFailedAndProcessedEntries_RemovesOnlyProcessed()
    {
        // Arrange - Create old failed and processed entries
        var oldFailed = new TestMessage { MessageId = Guid.NewGuid(), Content = "OldFailed" };
        var oldProcessed = new TestMessage { MessageId = Guid.NewGuid(), Content = "OldProcessed" };

        await _storage.AddAsync(oldFailed, new InboxOptions());
        await _storage.AddAsync(oldProcessed, new InboxOptions());

        // Mark as failed and processed respectively
        await _storage.MarkFailedAsync(oldFailed.MessageId.ToString(), "Processing error");
        await _storage.MarkProcessedAsync(oldProcessed.MessageId.ToString());

        _timeProvider.Advance(TimeSpan.FromDays(8));

        // Act - Cleanup entries older than 7 days
        await _storage.CleanupOldEntriesAsync(TimeSpan.FromDays(7));

        // Assert - Only processed entry should be removed, failed entry should remain
        var failedEntry = await _storage.GetAsync(oldFailed.MessageId.ToString());
        var processedEntry = await _storage.GetAsync(oldProcessed.MessageId.ToString());

        Assert.NotNull(failedEntry); // Failed entry should remain (not cleaned up)
        Assert.Null(processedEntry); // Processed entry should be removed
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsDuplicateAsync_WithWindowAtExactBoundary_ReturnsCorrectly()
    {
        // Arrange - Create a message and advance time to exactly the window boundary
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "BoundaryTest" };
        await _storage.AddAsync(message, new InboxOptions());

        var windowSize = TimeSpan.FromHours(1);
        _timeProvider.Advance(windowSize);

        var receivedAt = (await _storage.GetAsync(message.MessageId.ToString()))!.ReceivedAt;
        var cutoff = _timeProvider.GetUtcNow().Subtract(windowSize);

        // Act - Check at exact boundary: receivedAt should equal cutoff
        var isDuplicate = await _storage.IsDuplicateAsync(message.MessageId.ToString(), window: windowSize);

        // Assert - At exact boundary, entry.ReceivedAt == cutoff, so should still be True (>= comparison)
        // This verifies the >= operator behavior in line 49
        Assert.True(isDuplicate);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetUnprocessedAsync_WithMixedStatuses_ReturnsOnlyPending()
    {
        // Arrange
        var pending1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending1" };
        var pending2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending2" };
        var processed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var failed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        await _storage.AddAsync(pending1, new InboxOptions());
        await _storage.AddAsync(pending2, new InboxOptions());
        await _storage.AddAsync(processed, new InboxOptions());
        await _storage.AddAsync(failed, new InboxOptions());

        await _storage.MarkProcessedAsync(processed.MessageId.ToString());
        await _storage.MarkFailedAsync(failed.MessageId.ToString(), "Error");

        // Act
        var unprocessed = await _storage.GetUnprocessedAsync();

        // Assert
        Assert.Equal(2, unprocessed.Count());
        Assert.All(unprocessed, e => Assert.Equal(InboxStatus.Pending, e.Status));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingAsync_WithProcessingStatusFilter_ReturnsMatchingEntries()
    {
        // Arrange - Create entry and mark as processing (if supported)
        // Note: The code handles InboxEntryStatus.Processing mapping to InboxStatus.Processing
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processing" };
        await _storage.AddAsync(message, new InboxOptions());

        var query = new InboxQuery
        {
            Status = InboxEntryStatus.Processing,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert - Will return empty since we only set Pending/Processed/Failed/Duplicate
        // This verifies the Processing status branch is covered
        Assert.Empty(entries);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
