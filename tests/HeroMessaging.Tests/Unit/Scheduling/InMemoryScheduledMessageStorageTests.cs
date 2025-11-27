using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using Xunit;

namespace HeroMessaging.Tests.Unit.Scheduling;

/// <summary>
/// Unit tests for InMemoryScheduledMessageStorage
/// Target: 100% coverage for public APIs
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryScheduledMessageStorageTests
{
    private readonly InMemoryScheduledMessageStorage _storage;

    public InMemoryScheduledMessageStorageTests()
    {
        _storage = new InMemoryScheduledMessageStorage(TimeProvider.System);
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _storage.AddAsync(null!));
    }

    [Fact]
    public async Task AddAsync_WithValidMessage_AddsMessageToStorage()
    {
        // Arrange
        var message = CreateScheduledMessage();

        // Act
        var entry = await _storage.AddAsync(message);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.ScheduleId, entry.ScheduleId);
        Assert.Equal(message, entry.Message);
        Assert.Equal(ScheduledMessageStatus.Pending, entry.Status);
        Assert.True(entry.LastUpdated <= DateTimeOffset.UtcNow);
        Assert.True(entry.LastUpdated >= DateTimeOffset.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public async Task AddAsync_WithDuplicateScheduleId_ThrowsInvalidOperationException()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _storage.AddAsync(message));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task AddAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var message = CreateScheduledMessage();
        using var cts = new CancellationTokenSource();

        // Act
        var entry = await _storage.AddAsync(message, cts.Token);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(message.ScheduleId, entry.ScheduleId);
    }

    #endregion

    #region GetDueAsync Tests

    [Fact]
    public async Task GetDueAsync_WithNoDueMessages_ReturnsEmptyList()
    {
        // Arrange
        var futureMessage = CreateScheduledMessage(DateTimeOffset.UtcNow.AddHours(1));
        await _storage.AddAsync(futureMessage);

        // Act
        var dueMessages = await _storage.GetDueAsync(DateTimeOffset.UtcNow);

        // Assert
        Assert.Empty(dueMessages);
    }

    [Fact]
    public async Task GetDueAsync_WithDueMessages_ReturnsDueMessages()
    {
        // Arrange
        var pastMessage = CreateScheduledMessage(DateTimeOffset.UtcNow.AddMinutes(-5));
        var futureMessage = CreateScheduledMessage(DateTimeOffset.UtcNow.AddMinutes(5));

        await _storage.AddAsync(pastMessage);
        await _storage.AddAsync(futureMessage);

        // Act
        var dueMessages = await _storage.GetDueAsync(DateTimeOffset.UtcNow);

        // Assert
        Assert.Single(dueMessages);
        Assert.Equal(pastMessage.ScheduleId, dueMessages[0].ScheduleId);
    }

    [Fact]
    public async Task GetDueAsync_WithExactDeliveryTime_IncludesMessage()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var exactMessage = CreateScheduledMessage(now);
        await _storage.AddAsync(exactMessage);

        // Act
        var dueMessages = await _storage.GetDueAsync(now);

        // Assert
        Assert.Single(dueMessages);
    }

    [Fact]
    public async Task GetDueAsync_WithLimit_RespectsLimit()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            var message = CreateScheduledMessage(now.AddMinutes(-i - 1));
            await _storage.AddAsync(message);
        }

        // Act
        var dueMessages = await _storage.GetDueAsync(now, limit: 5);

        // Assert
        Assert.Equal(5, dueMessages.Count);
    }

    [Fact]
    public async Task GetDueAsync_OrdersByDeliverAtThenPriority()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        // Earlier time, lower priority
        var message1 = CreateScheduledMessage(now.AddMinutes(-10), priority: 1);
        // Earlier time, higher priority
        var message2 = CreateScheduledMessage(now.AddMinutes(-10), priority: 10);
        // Later time, highest priority
        var message3 = CreateScheduledMessage(now.AddMinutes(-5), priority: 100);

        await _storage.AddAsync(message3);
        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);

        // Act
        var dueMessages = await _storage.GetDueAsync(now);

        // Assert
        Assert.Equal(3, dueMessages.Count);
        // Should be ordered by DeliverAt (ascending), then Priority (descending)
        Assert.Equal(message2.ScheduleId, dueMessages[0].ScheduleId); // Earlier, higher priority
        Assert.Equal(message1.ScheduleId, dueMessages[1].ScheduleId); // Earlier, lower priority
        Assert.Equal(message3.ScheduleId, dueMessages[2].ScheduleId); // Later time
    }

    [Fact]
    public async Task GetDueAsync_OnlyReturnsPendingMessages()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var pendingMessage = CreateScheduledMessage(now.AddMinutes(-5));
        var deliveredMessage = CreateScheduledMessage(now.AddMinutes(-10));

        await _storage.AddAsync(pendingMessage);
        await _storage.AddAsync(deliveredMessage);
        await _storage.MarkDeliveredAsync(deliveredMessage.ScheduleId);

        // Act
        var dueMessages = await _storage.GetDueAsync(now);

        // Assert
        Assert.Single(dueMessages);
        Assert.Equal(pendingMessage.ScheduleId, dueMessages[0].ScheduleId);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithNonExistentScheduleId_ReturnsNull()
    {
        // Act
        var entry = await _storage.GetAsync(Guid.NewGuid());

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetAsync_WithValidScheduleId_ReturnsEntry()
    {
        // Arrange
        var message = CreateScheduledMessage();
        var addedEntry = await _storage.AddAsync(message);

        // Act
        var retrievedEntry = await _storage.GetAsync(message.ScheduleId);

        // Assert
        Assert.NotNull(retrievedEntry);
        Assert.Equal(addedEntry.ScheduleId, retrievedEntry.ScheduleId);
        Assert.Equal(addedEntry.Status, retrievedEntry.Status);
    }

    [Fact]
    public async Task GetAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        using var cts = new CancellationTokenSource();

        // Act
        var entry = await _storage.GetAsync(message.ScheduleId, cts.Token);

        // Assert
        Assert.NotNull(entry);
    }

    #endregion

    #region CancelAsync Tests

    [Fact]
    public async Task CancelAsync_WithNonExistentScheduleId_ReturnsFalse()
    {
        // Act
        var result = await _storage.CancelAsync(Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelAsync_WithPendingMessage_CancelsAndReturnsTrue()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);

        // Act
        var result = await _storage.CancelAsync(message.ScheduleId);

        // Assert
        Assert.True(result);

        var entry = await _storage.GetAsync(message.ScheduleId);
        Assert.NotNull(entry);
        Assert.Equal(ScheduledMessageStatus.Cancelled, entry.Status);
    }

    [Fact]
    public async Task CancelAsync_WithDeliveredMessage_ReturnsFalse()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        await _storage.MarkDeliveredAsync(message.ScheduleId);

        // Act
        var result = await _storage.CancelAsync(message.ScheduleId);

        // Assert
        Assert.False(result);

        var entry = await _storage.GetAsync(message.ScheduleId);
        Assert.NotNull(entry);
        Assert.Equal(ScheduledMessageStatus.Delivered, entry.Status); // Should remain Delivered
    }

    [Fact]
    public async Task CancelAsync_UpdatesLastUpdatedTime()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        await Task.Delay(10); // Small delay to ensure timestamp difference

        // Act
        var beforeCancel = DateTimeOffset.UtcNow;
        await _storage.CancelAsync(message.ScheduleId);
        var afterCancel = DateTimeOffset.UtcNow;

        // Assert
        var entry = await _storage.GetAsync(message.ScheduleId);
        Assert.NotNull(entry);
        Assert.True(entry.LastUpdated >= beforeCancel);
        Assert.True(entry.LastUpdated <= afterCancel);
    }

    #endregion

    #region MarkDeliveredAsync Tests

    [Fact]
    public async Task MarkDeliveredAsync_WithNonExistentScheduleId_ReturnsFalse()
    {
        // Act
        var result = await _storage.MarkDeliveredAsync(Guid.NewGuid());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkDeliveredAsync_WithValidScheduleId_MarksAsDeliveredAndReturnsTrue()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);

        // Act
        var beforeDelivery = DateTimeOffset.UtcNow;
        var result = await _storage.MarkDeliveredAsync(message.ScheduleId);
        var afterDelivery = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(result);

        var entry = await _storage.GetAsync(message.ScheduleId);
        Assert.NotNull(entry);
        Assert.Equal(ScheduledMessageStatus.Delivered, entry.Status);
        Assert.NotNull(entry.DeliveredAt);
        Assert.True(entry.DeliveredAt >= beforeDelivery);
        Assert.True(entry.DeliveredAt <= afterDelivery);
        Assert.True(entry.LastUpdated >= beforeDelivery);
        Assert.True(entry.LastUpdated <= afterDelivery);
    }

    [Fact]
    public async Task MarkDeliveredAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _storage.MarkDeliveredAsync(message.ScheduleId, cts.Token);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_WithNonExistentScheduleId_ReturnsFalse()
    {
        // Act
        var result = await _storage.MarkFailedAsync(Guid.NewGuid(), "Error message");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkFailedAsync_WithValidScheduleId_MarksAsFailedAndReturnsTrue()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        var errorMessage = "Test error message";

        // Act
        var beforeFailed = DateTimeOffset.UtcNow;
        var result = await _storage.MarkFailedAsync(message.ScheduleId, errorMessage);
        var afterFailed = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(result);

        var entry = await _storage.GetAsync(message.ScheduleId);
        Assert.NotNull(entry);
        Assert.Equal(ScheduledMessageStatus.Failed, entry.Status);
        Assert.Equal(errorMessage, entry.ErrorMessage);
        Assert.True(entry.LastUpdated >= beforeFailed);
        Assert.True(entry.LastUpdated <= afterFailed);
    }

    [Fact]
    public async Task MarkFailedAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var message = CreateScheduledMessage();
        await _storage.AddAsync(message);
        using var cts = new CancellationTokenSource();

        // Act
        var result = await _storage.MarkFailedAsync(message.ScheduleId, "Error", cts.Token);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetPendingCountAsync Tests

    [Fact]
    public async Task GetPendingCountAsync_WithNoMessages_ReturnsZero()
    {
        // Act
        var count = await _storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_WithOnlyPendingMessages_ReturnsCorrectCount()
    {
        // Arrange
        await _storage.AddAsync(CreateScheduledMessage());
        await _storage.AddAsync(CreateScheduledMessage());
        await _storage.AddAsync(CreateScheduledMessage());

        // Act
        var count = await _storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_WithMixedStatuses_ReturnsOnlyPendingCount()
    {
        // Arrange
        var message1 = CreateScheduledMessage();
        var message2 = CreateScheduledMessage();
        var message3 = CreateScheduledMessage();

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        await _storage.MarkDeliveredAsync(message1.ScheduleId);
        await _storage.CancelAsync(message2.ScheduleId);

        // Act
        var count = await _storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(1, count); // Only message3 is still pending
    }

    [Fact]
    public async Task GetPendingCountAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        await _storage.AddAsync(CreateScheduledMessage());
        using var cts = new CancellationTokenSource();

        // Act
        var count = await _storage.GetPendingCountAsync(cts.Token);

        // Assert
        Assert.Equal(1, count);
    }

    #endregion

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _storage.QueryAsync(null!));
    }

    [Fact]
    public async Task QueryAsync_WithNoFilters_ReturnsAllMessages()
    {
        // Arrange
        await _storage.AddAsync(CreateScheduledMessage());
        await _storage.AddAsync(CreateScheduledMessage());
        await _storage.AddAsync(CreateScheduledMessage());

        var query = new ScheduledMessageQuery();

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task QueryAsync_WithStatusFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var message1 = CreateScheduledMessage();
        var message2 = CreateScheduledMessage();
        var message3 = CreateScheduledMessage();

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        await _storage.MarkDeliveredAsync(message1.ScheduleId);
        await _storage.CancelAsync(message2.ScheduleId);

        var query = new ScheduledMessageQuery { Status = ScheduledMessageStatus.Pending };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Single(results);
        Assert.Equal(message3.ScheduleId, results[0].ScheduleId);
    }

    [Fact]
    public async Task QueryAsync_WithDestinationFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var message1 = CreateScheduledMessage(destination: "queue-a");
        var message2 = CreateScheduledMessage(destination: "queue-b");
        var message3 = CreateScheduledMessage(destination: "queue-a");

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        var query = new ScheduledMessageQuery { Destination = "queue-a" };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, entry => Assert.Equal("queue-a", entry.Message.Options.Destination));
    }

    [Fact]
    public async Task QueryAsync_WithMessageTypeFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var message1 = CreateScheduledMessage();
        var message2 = CreateScheduledMessage();

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);

        var query = new ScheduledMessageQuery { MessageType = nameof(TestMessage) };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task QueryAsync_WithDeliverAfterFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var message1 = CreateScheduledMessage(now.AddHours(-2));
        var message2 = CreateScheduledMessage(now.AddHours(-1));
        var message3 = CreateScheduledMessage(now.AddHours(1));

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        var query = new ScheduledMessageQuery { DeliverAfter = now.AddMinutes(-90) };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count); // message2 and message3
    }

    [Fact]
    public async Task QueryAsync_WithDeliverBeforeFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var message1 = CreateScheduledMessage(now.AddHours(-2));
        var message2 = CreateScheduledMessage(now.AddHours(-1));
        var message3 = CreateScheduledMessage(now.AddHours(1));

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        var query = new ScheduledMessageQuery { DeliverBefore = now };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count); // message1 and message2
    }

    [Fact]
    public async Task QueryAsync_WithOffsetAndLimit_ReturnsPaginatedResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _storage.AddAsync(CreateScheduledMessage());
        }

        var query = new ScheduledMessageQuery
        {
            Offset = 3,
            Limit = 5
        };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task QueryAsync_WithMultipleFilters_AppliesAllFilters()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var message1 = CreateScheduledMessage(now.AddHours(1), "queue-a");
        var message2 = CreateScheduledMessage(now.AddHours(2), "queue-b");
        var message3 = CreateScheduledMessage(now.AddHours(3), "queue-a");

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        await _storage.CancelAsync(message3.ScheduleId);

        var query = new ScheduledMessageQuery
        {
            Status = ScheduledMessageStatus.Pending,
            Destination = "queue-a",
            DeliverAfter = now
        };

        // Act
        var results = await _storage.QueryAsync(query);

        // Assert
        Assert.Single(results);
        Assert.Equal(message1.ScheduleId, results[0].ScheduleId);
    }

    #endregion

    #region CleanupAsync Tests

    [Fact]
    public async Task CleanupAsync_WithNoOldMessages_ReturnsZero()
    {
        // Arrange
        await _storage.AddAsync(CreateScheduledMessage());
        var olderThan = DateTimeOffset.UtcNow.AddDays(-1);

        // Act
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(0, removed);
    }

    [Fact]
    public async Task CleanupAsync_RemovesOldDeliveredMessages()
    {
        // Arrange
        var oldMessage = CreateScheduledMessage();
        await _storage.AddAsync(oldMessage);
        await _storage.MarkDeliveredAsync(oldMessage.ScheduleId);

        // Simulate old LastUpdated time by waiting and using future threshold
        await Task.Delay(10);
        var olderThan = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Act
        await Task.Delay(100); // Wait for threshold
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(1, removed);

        var entry = await _storage.GetAsync(oldMessage.ScheduleId);
        Assert.Null(entry);
    }

    [Fact]
    public async Task CleanupAsync_RemovesOldCancelledMessages()
    {
        // Arrange
        var oldMessage = CreateScheduledMessage();
        await _storage.AddAsync(oldMessage);
        await _storage.CancelAsync(oldMessage.ScheduleId);

        // Simulate old LastUpdated time
        await Task.Delay(10);
        var olderThan = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Act
        await Task.Delay(100);
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(1, removed);

        var entry = await _storage.GetAsync(oldMessage.ScheduleId);
        Assert.Null(entry);
    }

    [Fact]
    public async Task CleanupAsync_DoesNotRemovePendingMessages()
    {
        // Arrange
        var pendingMessage = CreateScheduledMessage();
        await _storage.AddAsync(pendingMessage);

        await Task.Delay(10);
        var olderThan = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Act
        await Task.Delay(100);
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(0, removed);

        var entry = await _storage.GetAsync(pendingMessage.ScheduleId);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task CleanupAsync_DoesNotRemoveFailedMessages()
    {
        // Arrange
        var failedMessage = CreateScheduledMessage();
        await _storage.AddAsync(failedMessage);
        await _storage.MarkFailedAsync(failedMessage.ScheduleId, "Error");

        // Note: Current implementation only cleans Delivered and Cancelled, not Failed
        await Task.Delay(10);
        var olderThan = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Act
        await Task.Delay(100);
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(0, removed); // Failed messages are not cleaned up

        var entry = await _storage.GetAsync(failedMessage.ScheduleId);
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task CleanupAsync_RemovesMultipleOldMessages()
    {
        // Arrange
        var message1 = CreateScheduledMessage();
        var message2 = CreateScheduledMessage();
        var message3 = CreateScheduledMessage();

        await _storage.AddAsync(message1);
        await _storage.AddAsync(message2);
        await _storage.AddAsync(message3);

        await _storage.MarkDeliveredAsync(message1.ScheduleId);
        await _storage.CancelAsync(message2.ScheduleId);
        // message3 remains pending

        await Task.Delay(10);
        var olderThan = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Act
        await Task.Delay(100);
        var removed = await _storage.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(2, removed); // message1 and message2

        Assert.Null(await _storage.GetAsync(message1.ScheduleId));
        Assert.Null(await _storage.GetAsync(message2.ScheduleId));
        Assert.NotNull(await _storage.GetAsync(message3.ScheduleId));
    }

    [Fact]
    public async Task CleanupAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var olderThan = DateTimeOffset.UtcNow.AddDays(-1);
        using var cts = new CancellationTokenSource();

        // Act
        var removed = await _storage.CleanupAsync(olderThan, cts.Token);

        // Assert
        Assert.Equal(0, removed);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentAddAsync_WithDifferentMessages_AllSucceed()
    {
        // Arrange
        var tasks = new List<Task<ScheduledMessageEntry>>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(_storage.AddAsync(CreateScheduledMessage()));
        }

        // Act
        var entries = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(100, entries.Length);
        Assert.All(entries, entry => Assert.NotNull(entry));

        var count = await _storage.GetPendingCountAsync();
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task ConcurrentOperations_MaintainConsistency()
    {
        // Arrange
        var messages = new List<ScheduledMessage>();
        for (int i = 0; i < 50; i++)
        {
            messages.Add(CreateScheduledMessage());
        }

        // Add all messages
        await Task.WhenAll(messages.Select(m => _storage.AddAsync(m)));

        // Act - Concurrent operations
        var tasks = new List<Task>();

        // Cancel half
        for (int i = 0; i < 25; i++)
        {
            tasks.Add(_storage.CancelAsync(messages[i].ScheduleId));
        }

        // Mark some as delivered
        for (int i = 25; i < 40; i++)
        {
            tasks.Add(_storage.MarkDeliveredAsync(messages[i].ScheduleId));
        }

        // Query while modifying
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_storage.GetPendingCountAsync());
        }

        await Task.WhenAll(tasks);

        // Assert - Final state should be consistent
        var finalCount = await _storage.GetPendingCountAsync();
        Assert.Equal(10, finalCount); // 50 - 25 cancelled - 15 delivered = 10 pending
    }

    #endregion

    #region Helper Methods

    private static ScheduledMessage CreateScheduledMessage(
        DateTimeOffset? deliverAt = null,
        string? destination = null,
        int priority = 0)
    {
        return new ScheduledMessage
        {
            ScheduleId = Guid.NewGuid(),
            Message = new TestMessage(),
            DeliverAt = deliverAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
            ScheduledAt = DateTimeOffset.UtcNow,
            Options = new SchedulingOptions
            {
                Destination = destination,
                Priority = priority
            }
        };
    }

    public sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
