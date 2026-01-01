using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.ErrorHandling;

public class InMemoryDeadLetterQueueTests
{
    private readonly Mock<ILogger<InMemoryDeadLetterQueue>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;
    private InMemoryDeadLetterQueue _queue = null!;

    public InMemoryDeadLetterQueueTests()
    {
        _loggerMock = new Mock<ILogger<InMemoryDeadLetterQueue>>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private InMemoryDeadLetterQueue CreateQueue()
    {
        return new InMemoryDeadLetterQueue(_loggerMock.Object, _timeProvider);
    }

    // Test helper message classes
    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class OtherMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryDeadLetterQueue(_loggerMock.Object, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendToDeadLetterAsync_WithValidMessage_StoresEntryAndReturnsId()
    {
        _queue = CreateQueue();
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "TestComponent",
            RetryCount = 3
        };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        Assert.NotNull(deadLetterId);
        Assert.NotEmpty(deadLetterId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendToDeadLetterAsync_WithValidMessage_LogsWarning()
    {
        _queue = CreateQueue();
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = new DeadLetterContext { Reason = "Test failure" };

        await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(messageId.ToString())),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLettersAsync_WithNoEntries_ReturnsEmptyCollection()
    {
        _queue = CreateQueue();

        var result = await _queue.GetDeadLettersAsync<TestMessage>();

        Assert.Empty(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLettersAsync_WithActiveEntries_ReturnsOnlyActiveEntries()
    {
        _queue = CreateQueue();
        var message1 = new TestMessage();
        var message2 = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var id1 = await _queue.SendToDeadLetterAsync(message1, context, TestContext.Current.CancellationToken);
        _ = await _queue.SendToDeadLetterAsync(message2, context, TestContext.Current.CancellationToken);

        // Mark one as retried
        await _queue.RetryAsync<TestMessage>(id1);

        var result = await _queue.GetDeadLettersAsync<TestMessage>();

        Assert.Single(result);
        Assert.Equal(message2.MessageId, result.First().Message.MessageId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLettersAsync_WithLimit_ReturnsLimitedResults()
    {
        _queue = CreateQueue();
        var context = new DeadLetterContext { Reason = "Test" };

        for (int i = 0; i < 10; i++)
        {
            await _queue.SendToDeadLetterAsync(new TestMessage(), context);
        }

        var result = await _queue.GetDeadLettersAsync<TestMessage>(limit: 5);

        Assert.Equal(5, result.Count());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLettersAsync_OrdersByCreatedAtDescending()
    {
        _queue = CreateQueue();
        var context = new DeadLetterContext { Reason = "Test" };

        var messageId1 = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId1 };
        await _queue.SendToDeadLetterAsync(message1, context, TestContext.Current.CancellationToken);

        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        var messageId2 = Guid.NewGuid();
        var message2 = new TestMessage { MessageId = messageId2 };
        await _queue.SendToDeadLetterAsync(message2, context, TestContext.Current.CancellationToken);

        var result = await _queue.GetDeadLettersAsync<TestMessage>();

        Assert.Equal(messageId2, result.First().Message.MessageId);
        Assert.Equal(messageId1, result.Last().Message.MessageId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryAsync_WithValidId_UpdatesStatusAndReturnsTrue()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        var result = await _queue.RetryAsync<TestMessage>(deadLetterId);

        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryAsync_WithValidId_SetsRetriedAtTimestamp()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await _queue.RetryAsync<TestMessage>(deadLetterId);

        var entries = await _queue.GetDeadLettersAsync<TestMessage>();
        // Entry should not appear in active list anymore
        Assert.Empty(entries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryAsync_WithInvalidId_ReturnsFalse()
    {
        _queue = CreateQueue();

        var result = await _queue.RetryAsync<TestMessage>("non-existent-id");

        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryAsync_WithWrongType_ReturnsFalse()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        // Try to retry with wrong message type
        var result = await _queue.RetryAsync<OtherMessage>(deadLetterId);

        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RetryAsync_LogsInformation()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        await _queue.RetryAsync<TestMessage>(deadLetterId);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(deadLetterId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscardAsync_WithValidId_UpdatesStatusAndReturnsTrue()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        var result = await _queue.DiscardAsync<TestMessage>(deadLetterId);

        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscardAsync_WithValidId_SetsDiscardedAtTimestamp()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await _queue.DiscardAsync<TestMessage>(deadLetterId);

        var entries = await _queue.GetDeadLettersAsync<TestMessage>();
        // Entry should not appear in active list anymore
        Assert.Empty(entries);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscardAsync_WithInvalidId_ReturnsFalse()
    {
        _queue = CreateQueue();

        var result = await _queue.DiscardAsync<TestMessage>("non-existent-id");

        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DiscardAsync_LogsInformation()
    {
        _queue = CreateQueue();
        var message = new TestMessage();
        var context = new DeadLetterContext { Reason = "Test" };

        var deadLetterId = await _queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        await _queue.DiscardAsync<TestMessage>(deadLetterId);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(deadLetterId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLetterCountAsync_WithNoEntries_ReturnsZero()
    {
        _queue = CreateQueue();

        var count = await _queue.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLetterCountAsync_WithActiveEntries_ReturnsCorrectCount()
    {
        _queue = CreateQueue();
        var context = new DeadLetterContext { Reason = "Test" };

        await _queue.SendToDeadLetterAsync(new TestMessage(), context);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        var count = await _queue.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDeadLetterCountAsync_ExcludesRetriedAndDiscardedEntries()
    {
        _queue = CreateQueue();
        var context = new DeadLetterContext { Reason = "Test" };

        var id1 = await _queue.SendToDeadLetterAsync(new TestMessage(), context);
        var id2 = await _queue.SendToDeadLetterAsync(new TestMessage(), context);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        await _queue.RetryAsync<TestMessage>(id1);
        await _queue.DiscardAsync<TestMessage>(id2);

        var count = await _queue.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_WithNoEntries_ReturnsEmptyStatistics()
    {
        _queue = CreateQueue();

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stats.TotalCount);
        Assert.Equal(0, stats.ActiveCount);
        Assert.Equal(0, stats.RetriedCount);
        Assert.Equal(0, stats.DiscardedCount);
        Assert.Null(stats.OldestEntry);
        Assert.Null(stats.NewestEntry);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_WithEntries_ReturnsCorrectStatistics()
    {
        _queue = CreateQueue();
        var context1 = new DeadLetterContext { Reason = "Error1", Component = "ComponentA" };
        var context2 = new DeadLetterContext { Reason = "Error2", Component = "ComponentB" };

        var id1 = await _queue.SendToDeadLetterAsync(new TestMessage(), context1);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context1);
        var id3 = await _queue.SendToDeadLetterAsync(new TestMessage(), context2);

        await _queue.RetryAsync<TestMessage>(id1);
        await _queue.DiscardAsync<TestMessage>(id3);

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(1, stats.ActiveCount);
        Assert.Equal(1, stats.RetriedCount);
        Assert.Equal(1, stats.DiscardedCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_GroupsByComponent()
    {
        _queue = CreateQueue();
        var contextA = new DeadLetterContext { Reason = "Error", Component = "ComponentA" };
        var contextB = new DeadLetterContext { Reason = "Error", Component = "ComponentB" };

        await _queue.SendToDeadLetterAsync(new TestMessage(), contextA);
        await _queue.SendToDeadLetterAsync(new TestMessage(), contextA);
        await _queue.SendToDeadLetterAsync(new TestMessage(), contextB);

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, stats.CountByComponent["ComponentA"]);
        Assert.Equal(1, stats.CountByComponent["ComponentB"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_GroupsByReason()
    {
        _queue = CreateQueue();
        var context1 = new DeadLetterContext { Reason = "ValidationError", Component = "Test" };
        var context2 = new DeadLetterContext { Reason = "TimeoutError", Component = "Test" };

        await _queue.SendToDeadLetterAsync(new TestMessage(), context1);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context1);
        await _queue.SendToDeadLetterAsync(new TestMessage(), context2);

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, stats.CountByReason["ValidationError"]);
        Assert.Equal(1, stats.CountByReason["TimeoutError"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_TruncatesLongReasons()
    {
        _queue = CreateQueue();
        var longReason = new string('A', 100);
        var context = new DeadLetterContext { Reason = longReason, Component = "Test" };

        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        var truncatedKey = stats.CountByReason.Keys.First();
        Assert.True(truncatedKey.Length <= 53); // 50 chars + "..."
        Assert.EndsWith("...", truncatedKey);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatisticsAsync_TracksOldestAndNewestEntries()
    {
        _queue = CreateQueue();
        var context = new DeadLetterContext { Reason = "Test", Component = "Test" };

        var startTime = _timeProvider.GetUtcNow();
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        var endTime = _timeProvider.GetUtcNow();
        await _queue.SendToDeadLetterAsync(new TestMessage(), context);

        var stats = await _queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(startTime, stats.OldestEntry);
        Assert.Equal(endTime, stats.NewestEntry);
    }
}
