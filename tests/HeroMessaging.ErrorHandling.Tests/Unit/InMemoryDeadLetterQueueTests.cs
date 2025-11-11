using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.ErrorHandling;

/// <summary>
/// Unit tests for InMemoryDeadLetterQueue
/// Tests dead letter queue functionality for failed message handling
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryDeadLetterQueueTests
{
    private readonly Mock<ILogger<InMemoryDeadLetterQueue>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryDeadLetterQueue _queue;

    public InMemoryDeadLetterQueueTests()
    {
        _loggerMock = new Mock<ILogger<InMemoryDeadLetterQueue>>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _queue = new InMemoryDeadLetterQueue(_loggerMock.Object, _timeProvider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryDeadLetterQueue(_loggerMock.Object, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region SendToDeadLetterAsync Tests

    [Fact]
    public async Task SendToDeadLetterAsync_WithMessage_CreatesEntry()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "TestComponent",
            RetryCount = 3
        };

        // Act
        var entryId = await _queue.SendToDeadLetterAsync(message, context);

        // Assert
        Assert.NotNull(entryId);
        Assert.NotEmpty(entryId);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var context = new DeadLetterContext { Reason = "Error" };
        var expectedTime = _timeProvider.GetUtcNow();

        // Act
        var entryId = await _queue.SendToDeadLetterAsync(message, context);
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        var entry = entries.FirstOrDefault(e => e.Id == entryId);
        Assert.NotNull(entry);
        Assert.Equal(expectedTime, entry.CreatedAt);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_SetsStatusToActive()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var context = new DeadLetterContext { Reason = "Error" };

        // Act
        var entryId = await _queue.SendToDeadLetterAsync(message, context);
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        var entry = entries.FirstOrDefault(e => e.Id == entryId);
        Assert.NotNull(entry);
        Assert.Equal(DeadLetterStatus.Active, entry.Status);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_LogsWarning()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var context = new DeadLetterContext { Reason = "Processing failed" };

        // Act
        await _queue.SendToDeadLetterAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("sent to dead letter queue")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetDeadLettersAsync Tests

    [Fact]
    public async Task GetDeadLettersAsync_WithNoEntries_ReturnsEmpty()
    {
        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithMultipleEntries_ReturnsAll()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Failed {i}" };
            var context = new DeadLetterContext { Reason = $"Error {i}" };
            await _queue.SendToDeadLetterAsync(message, context);
        }

        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task GetDeadLettersAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "First" };
        await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error 1" });

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Second" };
        await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error 2" });

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Third" };
        await _queue.SendToDeadLetterAsync(message3, new DeadLetterContext { Reason = "Error 3" });

        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert - Should return newest first
        var entryList = entries.ToList();
        Assert.Equal("Third", entryList[0].Message.Content);
        Assert.Equal("Second", entryList[1].Message.Content);
        Assert.Equal("First", entryList[2].Message.Content);
    }

    [Fact]
    public async Task GetDeadLettersAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Failed {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });
        }

        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>(limit: 5);

        // Assert
        Assert.Equal(5, entries.Count());
    }

    [Fact]
    public async Task GetDeadLettersAsync_ExcludesRetriedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Active" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retried" };

        var id1 = await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error 1" });
        var id2 = await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error 2" });

        await _queue.RetryAsync<TestMessage>(id2);

        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        Assert.Single(entries);
        Assert.Equal(id1, entries.First().Id);
    }

    [Fact]
    public async Task GetDeadLettersAsync_ExcludesDiscardedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Active" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Discarded" };

        var id1 = await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error 1" });
        var id2 = await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error 2" });

        await _queue.DiscardAsync<TestMessage>(id2);

        // Act
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();

        // Assert
        Assert.Single(entries);
        Assert.Equal(id1, entries.First().Id);
    }

    #endregion

    #region RetryAsync Tests

    [Fact]
    public async Task RetryAsync_WithValidId_MarksEntryAsRetried()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        // Act
        var result = await _queue.RetryAsync<TestMessage>(entryId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RetryAsync_WithValidId_SetsRetriedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var expectedRetriedAt = _timeProvider.GetUtcNow();

        // Act
        await _queue.RetryAsync<TestMessage>(entryId);

        // Assert - Entry should no longer appear in active list
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task RetryAsync_WithInvalidId_ReturnsFalse()
    {
        // Act
        var result = await _queue.RetryAsync<TestMessage>("non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RetryAsync_WithWrongMessageType_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        // Act - Try to retry with wrong type
        var result = await _queue.RetryAsync<AnotherTestMessage>(entryId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RetryAsync_LogsInformation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        // Act
        await _queue.RetryAsync<TestMessage>(entryId);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("marked for retry")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region DiscardAsync Tests

    [Fact]
    public async Task DiscardAsync_WithValidId_MarksEntryAsDiscarded()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        // Act
        var result = await _queue.DiscardAsync<TestMessage>(entryId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DiscardAsync_WithValidId_SetsDiscardedAtTimestamp()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        await _queue.DiscardAsync<TestMessage>(entryId);

        // Assert - Entry should no longer appear in active list
        var entries = await _queue.GetDeadLettersAsync<TestMessage>();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task DiscardAsync_WithInvalidId_ReturnsFalse()
    {
        // Act
        var result = await _queue.DiscardAsync<TestMessage>("non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DiscardAsync_LogsInformation()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Failed" };
        var entryId = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });

        // Act
        await _queue.DiscardAsync<TestMessage>(entryId);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("discarded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetDeadLetterCountAsync Tests

    [Fact]
    public async Task GetDeadLetterCountAsync_WithNoEntries_ReturnsZero()
    {
        // Act
        var count = await _queue.GetDeadLetterCountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_WithActiveEntries_ReturnsCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Failed {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error" });
        }

        // Act
        var count = await _queue.GetDeadLetterCountAsync();

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_ExcludesRetriedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Active" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retried" };

        await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error 1" });
        var id2 = await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error 2" });

        await _queue.RetryAsync<TestMessage>(id2);

        // Act
        var count = await _queue.GetDeadLetterCountAsync();

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_ExcludesDiscardedEntries()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Active" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Discarded" };

        await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error 1" });
        var id2 = await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error 2" });

        await _queue.DiscardAsync<TestMessage>(id2);

        // Act
        var count = await _queue.GetDeadLetterCountAsync();

        // Assert
        Assert.Equal(1, count);
    }

    #endregion

    #region GetStatisticsAsync Tests

    [Fact]
    public async Task GetStatisticsAsync_WithNoEntries_ReturnsEmptyStatistics()
    {
        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        Assert.Equal(0, stats.TotalCount);
        Assert.Equal(0, stats.ActiveCount);
        Assert.Equal(0, stats.RetriedCount);
        Assert.Equal(0, stats.DiscardedCount);
        Assert.Null(stats.OldestEntry);
        Assert.Null(stats.NewestEntry);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithMultipleEntries_ReturnsCorrectCounts()
    {
        // Arrange
        // Create 3 active, 2 retried, 1 discarded
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Active {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error", Component = "ComponentA" });
        }

        for (int i = 0; i < 2; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Retried {i}" };
            var id = await _queue.SendToDeadLetterAsync(message, new DeadLetterContext { Reason = "Error", Component = "ComponentB" });
            await _queue.RetryAsync<TestMessage>(id);
        }

        var discardMessage = new TestMessage { MessageId = Guid.NewGuid(), Content = "Discarded" };
        var discardId = await _queue.SendToDeadLetterAsync(discardMessage, new DeadLetterContext { Reason = "Error", Component = "ComponentC" });
        await _queue.DiscardAsync<TestMessage>(discardId);

        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        Assert.Equal(6, stats.TotalCount);
        Assert.Equal(3, stats.ActiveCount);
        Assert.Equal(2, stats.RetriedCount);
        Assert.Equal(1, stats.DiscardedCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_GroupsByComponent()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext
            {
                Reason = "Error",
                Component = "ComponentA"
            });
        }

        for (int i = 0; i < 2; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext
            {
                Reason = "Error",
                Component = "ComponentB"
            });
        }

        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        Assert.Equal(2, stats.CountByComponent.Count);
        Assert.Equal(3, stats.CountByComponent["ComponentA"]);
        Assert.Equal(2, stats.CountByComponent["ComponentB"]);
    }

    [Fact]
    public async Task GetStatisticsAsync_GroupsByReason()
    {
        // Arrange
        for (int i = 0; i < 2; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext
            {
                Reason = "Timeout error",
                Component = "Test"
            });
        }

        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _queue.SendToDeadLetterAsync(message, new DeadLetterContext
            {
                Reason = "Validation failed",
                Component = "Test"
            });
        }

        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        Assert.Equal(2, stats.CountByReason.Count);
        Assert.Equal(2, stats.CountByReason["Timeout error"]);
        Assert.Equal(3, stats.CountByReason["Validation failed"]);
    }

    [Fact]
    public async Task GetStatisticsAsync_TruncatesLongReasons()
    {
        // Arrange
        var longReason = new string('A', 100);
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        await _queue.SendToDeadLetterAsync(message, new DeadLetterContext
        {
            Reason = longReason,
            Component = "Test"
        });

        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        var reasonKey = stats.CountByReason.Keys.First();
        Assert.Equal(53, reasonKey.Length); // 50 chars + "..."
        Assert.EndsWith("...", reasonKey);
    }

    [Fact]
    public async Task GetStatisticsAsync_TracksOldestAndNewestEntries()
    {
        // Arrange
        var firstTime = _timeProvider.GetUtcNow();
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "First" };
        await _queue.SendToDeadLetterAsync(message1, new DeadLetterContext { Reason = "Error" });

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Middle" };
        await _queue.SendToDeadLetterAsync(message2, new DeadLetterContext { Reason = "Error" });

        _timeProvider.Advance(TimeSpan.FromHours(2));
        var lastTime = _timeProvider.GetUtcNow();
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Last" };
        await _queue.SendToDeadLetterAsync(message3, new DeadLetterContext { Reason = "Error" });

        // Act
        var stats = await _queue.GetStatisticsAsync();

        // Assert
        Assert.Equal(firstTime, stats.OldestEntry);
        Assert.Equal(lastTime, stats.NewestEntry);
    }

    #endregion

    #region Test Message Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    private class AnotherTestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int Data { get; set; }
    }

    #endregion
}
