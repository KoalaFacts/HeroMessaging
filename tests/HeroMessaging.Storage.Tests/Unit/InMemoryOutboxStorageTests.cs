using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

/// <summary>
/// Unit tests for InMemoryOutboxStorage
/// Tests outbox pattern implementation for reliable message publishing
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryOutboxStorageTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryOutboxStorage _storage;

    public InMemoryOutboxStorageTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _storage = new InMemoryOutboxStorage(_timeProvider);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithMessage_CreatesEntry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var options = new OutboxOptions { Priority = 1 };

        // Act
        var entry = await _storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.NotNull(entry.Id);
        Assert.Equal(OutboxStatus.Pending, entry.Status);
        Assert.Equal(message, entry.Message);
    }

    [Fact]
    public async Task AddAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Timestamped" };
        var options = new OutboxOptions();
        var expectedTime = _timeProvider.GetUtcNow();

        // Act
        var entry = await _storage.AddAsync(message, options);

        // Assert
        Assert.Equal(expectedTime, entry.CreatedAt);
    }

    #endregion

    #region GetPendingAsync Tests

    [Fact]
    public async Task GetPendingAsync_WithNoPendingMessages_ReturnsEmpty()
    {
        // Act
        var pending = await _storage.GetPendingAsync();

        // Assert
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_WithPendingMessages_ReturnsThem()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new OutboxOptions { Priority = i }, TestContext.Current.CancellationToken);
        }

        // Act
        var pending = await _storage.GetPendingAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, pending.Count());
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByPriorityThenCreatedAt()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Low Priority" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "High Priority" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Medium Priority" };

        await _storage.AddAsync(message1, new OutboxOptions { Priority = 10 }, TestContext.Current.CancellationToken);
        await _storage.AddAsync(message2, new OutboxOptions { Priority = 1 }, TestContext.Current.CancellationToken);
        await _storage.AddAsync(message3, new OutboxOptions { Priority = 5 }, TestContext.Current.CancellationToken);

        // Act
        var pending = await _storage.GetPendingAsync();

        // Assert
        var list = pending.ToList();
        Assert.Equal("High Priority", ((TestMessage)list[0].Message).Content);
        Assert.Equal("Medium Priority", ((TestMessage)list[1].Message).Content);
        Assert.Equal("Low Priority", ((TestMessage)list[2].Message).Content);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesProcessedMessages()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToProcess" };
        var entry1 = await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());
        await _storage.MarkProcessedAsync(entry2.Id);

        // Act
        var pending = await _storage.GetPendingAsync();

        // Assert
        Assert.Single(pending);
        Assert.Equal(entry1.Id, pending.First().Id);
    }

    [Fact]
    public async Task GetPendingAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new OutboxOptions());
        }

        // Act
        var pending = await _storage.GetPendingAsync(limit: 5);

        // Assert
        Assert.Equal(5, pending.Count());
    }

    #endregion

    #region MarkProcessedAsync Tests

    [Fact]
    public async Task MarkProcessedAsync_WithValidId_MarksAsProcessed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToComplete" };
        var entry = await _storage.AddAsync(message, new OutboxOptions());

        // Act
        var marked = await _storage.MarkProcessedAsync(entry.Id);

        // Assert
        Assert.True(marked);
        Assert.Equal(OutboxStatus.Processed, entry.Status);
        Assert.NotNull(entry.ProcessedAt);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithInvalidId_ReturnsFalse()
    {
        // Act
        var marked = await _storage.MarkProcessedAsync("invalid-id");

        // Assert
        Assert.False(marked);
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_WithValidId_MarksAsFailed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ToFail" };
        var entry = await _storage.AddAsync(message, new OutboxOptions());

        // Act
        var marked = await _storage.MarkFailedAsync(entry.Id, "Test error");

        // Assert
        Assert.True(marked);
        Assert.Equal(OutboxStatus.Failed, entry.Status);
        Assert.Equal("Test error", entry.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_WithInvalidId_ReturnsFalse()
    {
        // Act
        var marked = await _storage.MarkFailedAsync("invalid-id", "Error");

        // Assert
        Assert.False(marked);
    }

    #endregion

    #region UpdateRetryCountAsync Tests

    [Fact]
    public async Task UpdateRetryCountAsync_WithValidId_UpdatesRetryCount()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retry" };
        var entry = await _storage.AddAsync(message, new OutboxOptions());
        var nextRetry = _timeProvider.GetUtcNow().AddMinutes(5);

        // Act
        var updated = await _storage.UpdateRetryCountAsync(entry.Id, 1, nextRetry);

        // Assert
        Assert.True(updated);
        Assert.Equal(1, entry.RetryCount);
        Assert.Equal(nextRetry, entry.NextRetryAt);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_ExceedingMaxRetries_MarksFailed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ExceedRetries" };
        var entry = await _storage.AddAsync(message, new OutboxOptions { MaxRetries = 3 });

        // Act
        await _storage.UpdateRetryCountAsync(entry.Id, 3);

        // Assert
        Assert.Equal(OutboxStatus.Failed, entry.Status);
    }

    #endregion

    #region GetPendingCountAsync Tests

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new OutboxOptions());
        }

        // Act
        var count = await _storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_ExcludesProcessedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());
        await _storage.MarkProcessedAsync(entry2.Id);

        // Act
        var count = await _storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(1, count);
    }

    #endregion

    #region GetFailedAsync Tests

    [Fact]
    public async Task GetFailedAsync_ReturnsOnlyFailedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        await _storage.AddAsync(message1, new OutboxOptions());
        var failedEntry = await _storage.AddAsync(message2, new OutboxOptions());
        await _storage.MarkFailedAsync(failedEntry.Id, "Test error");

        // Act
        var failed = await _storage.GetFailedAsync();

        // Assert
        Assert.Single(failed);
        Assert.Equal("Failed", ((TestMessage)failed.First().Message).Content);
    }

    #endregion

    #region GetPendingAsync WithQuery Tests

    [Fact]
    public async Task GetPendingAsync_WithQuery_ReturnsPendingEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending1" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending2" };
        await _storage.AddAsync(message1, new OutboxOptions());
        await _storage.AddAsync(message2, new OutboxOptions());

        var query = new OutboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(2, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithStatusFilter_ReturnsMatchingEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        var entry1 = await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());
        var entry3 = await _storage.AddAsync(message3, new OutboxOptions());

        await _storage.MarkProcessedAsync(entry2.Id);
        await _storage.MarkFailedAsync(entry3.Id, "Error");

        var query = new OutboxQuery
        {
            Status = OutboxEntryStatus.Processed,
            Limit = 10
        };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(OutboxStatus.Processed, entries.First().Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithOlderThanFilter_ReturnsOldEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old1" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old2" };
        await _storage.AddAsync(message1, new OutboxOptions());
        await _storage.AddAsync(message2, new OutboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent" };
        await _storage.AddAsync(message3, new OutboxOptions());

        var cutoff = _timeProvider.GetUtcNow().AddHours(-1);
        var query = new OutboxQuery
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
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Old" };
        await _storage.AddAsync(message1, new OutboxOptions());

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent1" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Recent2" };
        await _storage.AddAsync(message2, new OutboxOptions());
        await _storage.AddAsync(message3, new OutboxOptions());

        var cutoff = _timeProvider.GetUtcNow().AddHours(-1);
        var query = new OutboxQuery
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
    public async Task GetPendingAsync_WithQueryLimit_ReturnsLimitedEntries()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.AddAsync(message, new OutboxOptions());
        }

        var query = new OutboxQuery { Limit = 5 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithNextRetryInFuture_ExcludesEntry()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Ready" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "NotReady" };

        var entry1 = await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());

        // Set next retry in the future for message2
        var futureRetry = _timeProvider.GetUtcNow().AddMinutes(10);
        await _storage.UpdateRetryCountAsync(entry2.Id, 1, futureRetry);

        var query = new OutboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(entry1.Id, entries.First().Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithNextRetryInPast_IncludesEntry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "ReadyForRetry" };
        var entry = await _storage.AddAsync(message, new OutboxOptions());

        // Set next retry in the past
        var pastRetry = _timeProvider.GetUtcNow().AddMinutes(-10);
        await _storage.UpdateRetryCountAsync(entry.Id, 1, pastRetry);

        var query = new OutboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(entry.Id, entries.First().Id);
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByPriorityThenCreatedAtWithQuery()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Low" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "High" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Medium" };

        await _storage.AddAsync(message1, new OutboxOptions { Priority = 10 });
        await _storage.AddAsync(message2, new OutboxOptions { Priority = 1 });
        await _storage.AddAsync(message3, new OutboxOptions { Priority = 5 });

        var query = new OutboxQuery { Limit = 10 };

        // Act
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        var list = entries.ToList();
        Assert.Equal("High", ((TestMessage)list[0].Message).Content);
        Assert.Equal("Medium", ((TestMessage)list[1].Message).Content);
        Assert.Equal("Low", ((TestMessage)list[2].Message).Content);
    }

    [Fact]
    public async Task GetPendingAsync_WithAllStatusFilters_ReturnsCorrectEntries()
    {
        // Arrange
        var pending = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var processed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var failed = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        var entry1 = await _storage.AddAsync(pending, new OutboxOptions());
        var entry2 = await _storage.AddAsync(processed, new OutboxOptions());
        var entry3 = await _storage.AddAsync(failed, new OutboxOptions());

        await _storage.MarkProcessedAsync(entry2.Id);
        await _storage.MarkFailedAsync(entry3.Id, "Error");

        // Test Pending status
        var pendingQuery = new OutboxQuery { Status = OutboxEntryStatus.Pending, Limit = 10 };
        var pendingEntries = await _storage.GetPendingAsync(pendingQuery);
        Assert.Single(pendingEntries);
        Assert.Equal(OutboxStatus.Pending, pendingEntries.First().Status);

        // Test Processed status
        var processedQuery = new OutboxQuery { Status = OutboxEntryStatus.Processed, Limit = 10 };
        var processedEntries = await _storage.GetPendingAsync(processedQuery);
        Assert.Single(processedEntries);
        Assert.Equal(OutboxStatus.Processed, processedEntries.First().Status);

        // Test Failed status
        var failedQuery = new OutboxQuery { Status = OutboxEntryStatus.Failed, Limit = 10 };
        var failedEntries = await _storage.GetPendingAsync(failedQuery);
        Assert.Single(failedEntries);
        Assert.Equal(OutboxStatus.Failed, failedEntries.First().Status);
    }

    #endregion

    #region Retry Handling Tests

    [Fact]
    public async Task UpdateRetryCountAsync_WithInvalidId_ReturnsFalse()
    {
        // Act
        var updated = await _storage.UpdateRetryCountAsync("invalid-id", 1);

        // Assert
        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_BelowMaxRetries_KeepsPendingStatus()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retry" };
        var entry = await _storage.AddAsync(message, new OutboxOptions { MaxRetries = 5 });

        // Act
        await _storage.UpdateRetryCountAsync(entry.Id, 2);

        // Assert
        Assert.Equal(OutboxStatus.Pending, entry.Status);
        Assert.Equal(2, entry.RetryCount);
    }

    [Fact]
    public async Task GetPendingAsync_WithoutLimit_ExcludesNotReadyRetries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Ready" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "NotReady" };

        var entry1 = await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());

        // Set next retry in the future
        var futureRetry = _timeProvider.GetUtcNow().AddMinutes(10);
        await _storage.UpdateRetryCountAsync(entry2.Id, 1, futureRetry);

        // Act
        var entries = await _storage.GetPendingAsync();

        // Assert
        Assert.Single(entries);
        Assert.Equal(entry1.Id, entries.First().Id);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryOutboxStorage(null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Additional Coverage Tests

    [Fact]
    public async Task GetPendingAsync_WithDefaultStatusWhenStatusNotSpecified_ReturnsPendingOnly()
    {
        // Arrange
        var pendingMsg = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var processedMsg = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };
        var failedMsg = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };

        var entry1 = await _storage.AddAsync(pendingMsg, new OutboxOptions());
        var entry2 = await _storage.AddAsync(processedMsg, new OutboxOptions());
        var entry3 = await _storage.AddAsync(failedMsg, new OutboxOptions());

        await _storage.MarkProcessedAsync(entry2.Id);
        await _storage.MarkFailedAsync(entry3.Id, "Error");

        // Act - Query with no status filter (should default to Pending)
        var query = new OutboxQuery { Status = null, Limit = 10 };
        var entries = await _storage.GetPendingAsync(query);

        // Assert - Should only return pending entry
        Assert.Single(entries);
        Assert.Equal(entry1.Id, entries.First().Id);
        Assert.Equal(OutboxStatus.Pending, entries.First().Status);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_AtExactMaxRetriesThreshold_TransitionsToFailed()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "MaxRetries" };
        var maxRetries = 3;
        var entry = await _storage.AddAsync(message, new OutboxOptions { MaxRetries = maxRetries });

        // Act - Set retry count to exactly MaxRetries (transition boundary)
        var result = await _storage.UpdateRetryCountAsync(entry.Id, maxRetries, null);

        // Assert
        Assert.True(result);
        Assert.Equal(maxRetries, entry.RetryCount);
        Assert.Equal(OutboxStatus.Failed, entry.Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithProcessingStatus_ReturnsOnlyProcessingEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Pending" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processing" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Processed" };

        var entry1 = await _storage.AddAsync(message1, new OutboxOptions());
        var entry2 = await _storage.AddAsync(message2, new OutboxOptions());
        var entry3 = await _storage.AddAsync(message3, new OutboxOptions());

        // Manually set one to Processing status (testing the switch case coverage)
        entry2.Status = OutboxStatus.Processing;

        // Act
        var query = new OutboxQuery { Status = OutboxEntryStatus.Processing, Limit = 10 };
        var entries = await _storage.GetPendingAsync(query);

        // Assert
        Assert.Single(entries);
        Assert.Equal(entry2.Id, entries.First().Id);
        Assert.Equal(OutboxStatus.Processing, entries.First().Status);
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
