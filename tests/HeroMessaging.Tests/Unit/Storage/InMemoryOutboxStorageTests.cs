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
        var expectedTime = _timeProvider.GetUtcNow().DateTime;

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
        var nextRetry = _timeProvider.GetUtcNow().DateTime.AddMinutes(5);

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
