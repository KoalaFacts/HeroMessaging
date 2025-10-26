using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Scheduling;

/// <summary>
/// Unit tests for InMemoryScheduler
/// Target: 100% coverage for public APIs, execution time < 10s
/// </summary>
public class InMemorySchedulerTests : IAsyncLifetime
{
    private InMemoryScheduler? _scheduler;
    private TestMessageDeliveryHandler? _deliveryHandler;

    public ValueTask InitializeAsync()
    {
        _deliveryHandler = new TestMessageDeliveryHandler();
        _scheduler = new InMemoryScheduler(_deliveryHandler);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _scheduler?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region ScheduleAsync with TimeSpan Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithValidDelay_ReturnsSuccessResult()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var delay = TimeSpan.FromMilliseconds(100);

        // Act
        var result = await _scheduler!.ScheduleAsync(message, delay);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.True(result.ScheduledFor > DateTimeOffset.UtcNow);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var delay = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _scheduler!.ScheduleAsync<IMessage>(null!, delay));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithNegativeDelay_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Test message");
        var delay = TimeSpan.FromMilliseconds(-100);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _scheduler!.ScheduleAsync(message, delay));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithZeroDelay_DeliversImmediately()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Immediate message");
        var delay = TimeSpan.Zero;

        // Act
        var result = await _scheduler!.ScheduleAsync(message, delay);
        await Task.Delay(100); // Give it time to deliver

        // Assert
        Assert.True(result.Success);
        Assert.Single(_deliveryHandler!.DeliveredMessages);
        Assert.Equal(message.MessageId, _deliveryHandler.DeliveredMessages[0].MessageId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithShortDelay_DeliversAfterDelay()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Delayed message");
        var delay = TimeSpan.FromMilliseconds(50);

        // Act
        var result = await _scheduler!.ScheduleAsync(message, delay);

        // Assert - message should not be delivered yet
        Assert.Empty(_deliveryHandler!.DeliveredMessages);

        // Wait for delivery
        await Task.Delay(150);

        // Assert - message should be delivered now
        Assert.Single(_deliveryHandler.DeliveredMessages);
        Assert.Equal(message.MessageId, _deliveryHandler.DeliveredMessages[0].MessageId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithOptions_StoresOptionsCorrectly()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Message with options");
        var delay = TimeSpan.FromMilliseconds(100);
        var options = new SchedulingOptions
        {
            Destination = "test-queue",
            Priority = 5,
            Metadata = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var result = await _scheduler!.ScheduleAsync(message, delay, options);

        // Assert
        Assert.True(result.Success);
        var scheduled = await _scheduler.GetScheduledAsync(result.ScheduleId);
        Assert.NotNull(scheduled);
        Assert.Equal("test-queue", scheduled.Destination);
        Assert.Equal(5, scheduled.Priority);
    }

    #endregion

    #region ScheduleAsync with DateTimeOffset Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithFutureTime_ReturnsSuccessResult()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Future message");
        var deliverAt = DateTimeOffset.UtcNow.AddSeconds(1);

        // Act
        var result = await _scheduler!.ScheduleAsync(message, deliverAt);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.Equal(deliverAt, result.ScheduledFor, TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithPastTime_ThrowsArgumentException()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Past message");
        var deliverAt = DateTimeOffset.UtcNow.AddSeconds(-2); // More than 1 second in past

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _scheduler!.ScheduleAsync(message, deliverAt));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithNullMessageAndDateTimeOffset_ThrowsArgumentNullException()
    {
        // Arrange
        var deliverAt = DateTimeOffset.UtcNow.AddSeconds(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _scheduler!.ScheduleAsync<IMessage>(null!, deliverAt));
    }

    #endregion

    #region CancelScheduledAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CancelScheduledAsync_WithValidScheduleId_ReturnsTrueAndPreventsDelivery()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Cancellable message");
        var delay = TimeSpan.FromSeconds(1);
        var result = await _scheduler!.ScheduleAsync(message, delay);

        // Act
        var cancelled = await _scheduler.CancelScheduledAsync(result.ScheduleId);
        await Task.Delay(1500); // Wait past delivery time

        // Assert
        Assert.True(cancelled);
        Assert.Empty(_deliveryHandler!.DeliveredMessages);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CancelScheduledAsync_WithInvalidScheduleId_ReturnsFalse()
    {
        // Act
        var cancelled = await _scheduler!.CancelScheduledAsync(Guid.NewGuid());

        // Assert
        Assert.False(cancelled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CancelScheduledAsync_WithAlreadyDeliveredMessage_ReturnsFalse()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Fast message");
        var delay = TimeSpan.FromMilliseconds(50);
        var result = await _scheduler!.ScheduleAsync(message, delay);

        await Task.Delay(150); // Wait for delivery

        // Act
        var cancelled = await _scheduler.CancelScheduledAsync(result.ScheduleId);

        // Assert
        Assert.False(cancelled);
    }

    #endregion

    #region GetScheduledAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetScheduledAsync_WithValidScheduleId_ReturnsMessageInfo()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Query message");
        var delay = TimeSpan.FromMilliseconds(500);
        var result = await _scheduler!.ScheduleAsync(message, delay);

        // Act
        var info = await _scheduler.GetScheduledAsync(result.ScheduleId);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(result.ScheduleId, info.ScheduleId);
        Assert.Equal(message.MessageId, info.MessageId);
        Assert.Equal(ScheduledMessageStatus.Pending, info.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetScheduledAsync_WithInvalidScheduleId_ReturnsNull()
    {
        // Act
        var info = await _scheduler!.GetScheduledAsync(Guid.NewGuid());

        // Assert
        Assert.Null(info);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetScheduledAsync_AfterDelivery_ShowsDeliveredStatus()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Delivered message");
        var delay = TimeSpan.FromMilliseconds(50);
        var result = await _scheduler!.ScheduleAsync(message, delay);

        await Task.Delay(150); // Wait for delivery

        // Act
        var info = await _scheduler.GetScheduledAsync(result.ScheduleId);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(ScheduledMessageStatus.Delivered, info.Status);
    }

    #endregion

    #region GetPendingAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingAsync_WithNoPendingMessages_ReturnsEmptyList()
    {
        // Act
        var pending = await _scheduler!.GetPendingAsync();

        // Assert
        Assert.Empty(pending);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingAsync_WithMultiplePendingMessages_ReturnsAll()
    {
        // Arrange
        var message1 = TestMessageBuilder.CreateValidMessage("Message 1");
        var message2 = TestMessageBuilder.CreateValidMessage("Message 2");
        var message3 = TestMessageBuilder.CreateValidMessage("Message 3");

        await _scheduler!.ScheduleAsync(message1, TimeSpan.FromSeconds(10));
        await _scheduler.ScheduleAsync(message2, TimeSpan.FromSeconds(20));
        await _scheduler.ScheduleAsync(message3, TimeSpan.FromSeconds(30));

        // Act
        var pending = await _scheduler.GetPendingAsync();

        // Assert
        Assert.Equal(3, pending.Count);
        Assert.All(pending, info => Assert.Equal(ScheduledMessageStatus.Pending, info.Status));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingAsync_WithQuery_FiltersCorrectly()
    {
        // Arrange
        var message1 = TestMessageBuilder.CreateValidMessage("Message 1");
        var message2 = TestMessageBuilder.CreateValidMessage("Message 2");

        await _scheduler!.ScheduleAsync(message1, TimeSpan.FromSeconds(1),
            new SchedulingOptions { Destination = "queue-a" });
        await _scheduler.ScheduleAsync(message2, TimeSpan.FromSeconds(2),
            new SchedulingOptions { Destination = "queue-b" });

        var query = new ScheduledMessageQuery { Destination = "queue-a", Limit = 10 };

        // Act
        var pending = await _scheduler.GetPendingAsync(query);

        // Assert
        Assert.Single(pending);
        Assert.Equal("queue-a", pending[0].Destination);
    }

    #endregion

    #region GetPendingCountAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingCountAsync_WithNoPendingMessages_ReturnsZero()
    {
        // Act
        var count = await _scheduler!.GetPendingCountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingCountAsync_WithPendingMessages_ReturnsCorrectCount()
    {
        // Arrange
        var message1 = TestMessageBuilder.CreateValidMessage("Message 1");
        var message2 = TestMessageBuilder.CreateValidMessage("Message 2");

        await _scheduler!.ScheduleAsync(message1, TimeSpan.FromSeconds(10));
        await _scheduler.ScheduleAsync(message2, TimeSpan.FromSeconds(20));

        // Act
        var count = await _scheduler.GetPendingCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetPendingCountAsync_AfterDelivery_DecreasesCount()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("Fast message");
        await _scheduler!.ScheduleAsync(message, TimeSpan.FromMilliseconds(50));

        var initialCount = await _scheduler.GetPendingCountAsync();
        await Task.Delay(150); // Wait for delivery

        // Act
        var finalCount = await _scheduler.GetPendingCountAsync();

        // Assert
        Assert.Equal(1, initialCount);
        Assert.Equal(0, finalCount);
    }

    #endregion

    #region Concurrent Operations Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScheduleAsync_WithConcurrentCalls_HandlesAllCorrectly()
    {
        // Arrange
        var tasks = new List<Task<ScheduleResult>>();
        for (int i = 0; i < 100; i++)
        {
            var message = TestMessageBuilder.CreateValidMessage($"Concurrent message {i}");
            tasks.Add(_scheduler!.ScheduleAsync(message, TimeSpan.FromSeconds(10)));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(100, results.Select(r => r.ScheduleId).Distinct().Count());

        var count = await _scheduler!.GetPendingCountAsync();
        Assert.Equal(100, count);
    }

    #endregion
}

/// <summary>
/// Test helper to capture delivered messages
/// </summary>
public class TestMessageDeliveryHandler : IMessageDeliveryHandler
{
    public List<IMessage> DeliveredMessages { get; } = new();
    public List<(Guid ScheduleId, Exception Exception)> FailedDeliveries { get; } = new();

    public Task DeliverAsync(ScheduledMessage scheduledMessage, CancellationToken cancellationToken = default)
    {
        DeliveredMessages.Add(scheduledMessage.Message);
        return Task.CompletedTask;
    }

    public Task HandleDeliveryFailureAsync(Guid scheduleId, Exception exception, CancellationToken cancellationToken = default)
    {
        FailedDeliveries.Add((scheduleId, exception));
        return Task.CompletedTask;
    }
}
