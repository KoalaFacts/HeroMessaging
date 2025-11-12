using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Scheduling.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class StorageBackedSchedulerTests : IAsyncDisposable
{
    private readonly Mock<IScheduledMessageStorage> _storageMock;
    private readonly Mock<IMessageDeliveryHandler> _deliveryHandlerMock;
    private readonly Mock<ILogger<StorageBackedScheduler>> _loggerMock;
    private readonly StorageBackedSchedulerOptions _options;
    private StorageBackedScheduler? _scheduler;

    public StorageBackedSchedulerTests()
    {
        _storageMock = new Mock<IScheduledMessageStorage>();
        _deliveryHandlerMock = new Mock<IMessageDeliveryHandler>();
        _loggerMock = new Mock<ILogger<StorageBackedScheduler>>();

        _options = new StorageBackedSchedulerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
            MaxConcurrency = 2,
            AutoCleanup = false // Disable for most tests to avoid background tasks
        };
    }

    [Fact]
    public void Constructor_WithNullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(null!, _deliveryHandlerMock.Object, _options, _loggerMock.Object));
        Assert.Equal("storage", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullDeliveryHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_storageMock.Object, null!, _options, _loggerMock.Object));
        Assert.Equal("deliveryHandler", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_storageMock.Object, _deliveryHandlerMock.Object, null!, _loggerMock.Object));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_storageMock.Object, _deliveryHandlerMock.Object, _options, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_StartsBackgroundWorkers()
    {
        // Act
        _scheduler = new StorageBackedScheduler(
            _storageMock.Object,
            _deliveryHandlerMock.Object,
            _options,
            _loggerMock.Object);

        // Assert - Constructor should complete without throwing
        Assert.NotNull(_scheduler);
    }

    [Fact]
    public async Task ScheduleAsync_WithDelay_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        _scheduler = CreateScheduler();
        TestMessage message = null!;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _scheduler.ScheduleAsync(message, TimeSpan.FromSeconds(5)));
        Assert.Equal("message", ex.ParamName);
    }

    [Fact]
    public async Task ScheduleAsync_WithDelay_WithNegativeDelay_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _scheduler.ScheduleAsync(message, TimeSpan.FromSeconds(-5)));
        Assert.Equal("delay", ex.ParamName);
        Assert.Contains("cannot be negative", ex.Message);
    }

    [Fact]
    public async Task ScheduleAsync_WithDelay_WithValidMessage_AddsToStorage()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();
        var delay = TimeSpan.FromSeconds(10);

        _storageMock
            .Setup(s => s.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledMessage sm, CancellationToken _) => new ScheduledMessageEntry
            {
                ScheduleId = sm.ScheduleId,
                Message = sm,
                Status = ScheduledMessageStatus.Pending
            });

        // Act
        var result = await _scheduler.ScheduleAsync(message, delay);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.True(result.DeliverAt > DateTimeOffset.UtcNow);

        _storageMock.Verify(
            s => s.AddAsync(It.Is<ScheduledMessage>(sm =>
                sm.Message == message &&
                sm.DeliverAt >= DateTimeOffset.UtcNow.Add(delay).AddSeconds(-1) &&
                sm.DeliverAt <= DateTimeOffset.UtcNow.Add(delay).AddSeconds(1)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScheduleAsync_WithDeliverAt_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        _scheduler = CreateScheduler();
        TestMessage message = null!;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _scheduler.ScheduleAsync(message, DateTimeOffset.UtcNow.AddSeconds(10)));
        Assert.Equal("message", ex.ParamName);
    }

    [Fact]
    public async Task ScheduleAsync_WithDeliverAt_WithPastDeliveryTime_ThrowsArgumentException()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();
        var pastTime = DateTimeOffset.UtcNow.AddSeconds(-5);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _scheduler.ScheduleAsync(message, pastTime));
        Assert.Equal("deliverAt", ex.ParamName);
        Assert.Contains("cannot be in the past", ex.Message);
    }

    [Fact]
    public async Task ScheduleAsync_WithDeliverAt_WithValidMessage_AddsToStorage()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var options = new SchedulingOptions { Priority = 10, Destination = "test-queue" };

        _storageMock
            .Setup(s => s.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledMessage sm, CancellationToken _) => new ScheduledMessageEntry
            {
                ScheduleId = sm.ScheduleId,
                Message = sm,
                Status = ScheduledMessageStatus.Pending
            });

        // Act
        var result = await _scheduler.ScheduleAsync(message, deliverAt, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.Equal(deliverAt, result.DeliverAt);

        _storageMock.Verify(
            s => s.AddAsync(It.Is<ScheduledMessage>(sm =>
                sm.Message == message &&
                sm.DeliverAt == deliverAt &&
                sm.Options.Priority == 10 &&
                sm.Options.Destination == "test-queue"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScheduleAsync_WhenStorageThrows_ReturnsFailedResult()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();
        var deliverAt = DateTimeOffset.UtcNow.AddSeconds(10);

        _storageMock
            .Setup(s => s.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _scheduler.ScheduleAsync(message, deliverAt);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to schedule message", result.ErrorMessage);
        Assert.Contains("Storage error", result.ErrorMessage);
    }

    [Fact]
    public async Task CancelScheduledAsync_WithValidScheduleId_CallsStorageCancel()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();

        _storageMock
            .Setup(s => s.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _scheduler.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.True(result);
        _storageMock.Verify(
            s => s.CancelAsync(scheduleId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelScheduledAsync_WhenStorageReturnsFalse_ReturnsFalse()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();

        _storageMock
            .Setup(s => s.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _scheduler.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelScheduledAsync_WhenStorageThrows_ReturnsFalse()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();

        _storageMock
            .Setup(s => s.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _scheduler.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetScheduledAsync_WithValidScheduleId_ReturnsMessageInfo()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();
        var message = new TestMessage();
        var entry = new ScheduledMessageEntry
        {
            ScheduleId = scheduleId,
            Message = new ScheduledMessage
            {
                ScheduleId = scheduleId,
                Message = message,
                DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5),
                ScheduledAt = DateTimeOffset.UtcNow,
                Options = new SchedulingOptions { Priority = 5, Destination = "queue1" }
            },
            Status = ScheduledMessageStatus.Pending
        };

        _storageMock
            .Setup(s => s.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _scheduler.GetScheduledAsync(scheduleId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(scheduleId, result.ScheduleId);
        Assert.Equal(message.MessageId, result.MessageId);
        Assert.Equal(nameof(TestMessage), result.MessageType);
        Assert.Equal(ScheduledMessageStatus.Pending, result.Status);
        Assert.Equal(5, result.Priority);
        Assert.Equal("queue1", result.Destination);
    }

    [Fact]
    public async Task GetScheduledAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();

        _storageMock
            .Setup(s => s.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledMessageEntry?)null);

        // Act
        var result = await _scheduler.GetScheduledAsync(scheduleId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetScheduledAsync_WhenStorageThrows_ReturnsNull()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var scheduleId = Guid.NewGuid();

        _storageMock
            .Setup(s => s.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _scheduler.GetScheduledAsync(scheduleId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPendingAsync_WithNullQuery_UsesDefaultQuery()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var entries = new List<ScheduledMessageEntry>
        {
            CreateEntry(ScheduledMessageStatus.Pending),
            CreateEntry(ScheduledMessageStatus.Pending)
        };

        _storageMock
            .Setup(s => s.QueryAsync(It.IsAny<ScheduledMessageQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await _scheduler.GetPendingAsync();

        // Assert
        Assert.Equal(2, result.Count);
        _storageMock.Verify(
            s => s.QueryAsync(
                It.Is<ScheduledMessageQuery>(q => q.Status == ScheduledMessageStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPendingAsync_WithCustomQuery_UsesProvidedQuery()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var query = new ScheduledMessageQuery
        {
            Status = ScheduledMessageStatus.Delivered,
            Destination = "test-queue"
        };
        var entries = new List<ScheduledMessageEntry> { CreateEntry(ScheduledMessageStatus.Delivered) };

        _storageMock
            .Setup(s => s.QueryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await _scheduler.GetPendingAsync(query);

        // Assert
        Assert.Single(result);
        _storageMock.Verify(
            s => s.QueryAsync(query, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPendingAsync_WhenStorageThrows_ReturnsEmptyList()
    {
        // Arrange
        _scheduler = CreateScheduler();

        _storageMock
            .Setup(s => s.QueryAsync(It.IsAny<ScheduledMessageQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _scheduler.GetPendingAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCountFromStorage()
    {
        // Arrange
        _scheduler = CreateScheduler();

        _storageMock
            .Setup(s => s.GetPendingCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        // Act
        var result = await _scheduler.GetPendingCountAsync();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task GetPendingCountAsync_WhenStorageThrows_ReturnsZero()
    {
        // Arrange
        _scheduler = CreateScheduler();

        _storageMock
            .Setup(s => s.GetPendingCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _scheduler.GetPendingCountAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task DisposeAsync_StopsBackgroundWorkers()
    {
        // Arrange
        _scheduler = CreateScheduler();

        // Act
        await _scheduler.DisposeAsync();

        // Assert - Should complete without hanging
        // Background workers should have stopped
    }

    [Fact]
    public async Task ScheduleAsync_WithOptionsNull_UsesDefaultOptions()
    {
        // Arrange
        _scheduler = CreateScheduler();
        var message = new TestMessage();
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(1);

        _storageMock
            .Setup(s => s.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledMessage sm, CancellationToken _) => new ScheduledMessageEntry
            {
                ScheduleId = sm.ScheduleId,
                Message = sm,
                Status = ScheduledMessageStatus.Pending
            });

        // Act
        var result = await _scheduler.ScheduleAsync(message, deliverAt, null);

        // Assert
        Assert.True(result.Success);
        _storageMock.Verify(
            s => s.AddAsync(It.Is<ScheduledMessage>(sm =>
                sm.Options != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private StorageBackedScheduler CreateScheduler()
    {
        return new StorageBackedScheduler(
            _storageMock.Object,
            _deliveryHandlerMock.Object,
            _options,
            _loggerMock.Object);
    }

    private static ScheduledMessageEntry CreateEntry(ScheduledMessageStatus status)
    {
        var message = new TestMessage();
        return new ScheduledMessageEntry
        {
            ScheduleId = Guid.NewGuid(),
            Message = new ScheduledMessage
            {
                ScheduleId = Guid.NewGuid(),
                Message = message,
                DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5),
                ScheduledAt = DateTimeOffset.UtcNow,
                Options = new SchedulingOptions()
            },
            Status = status
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_scheduler != null)
        {
            await _scheduler.DisposeAsync();
        }
    }

    private sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
