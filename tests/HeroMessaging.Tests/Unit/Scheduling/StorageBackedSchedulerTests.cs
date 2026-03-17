using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Scheduling;

[Trait("Category", "Unit")]
public class StorageBackedSchedulerTests : IAsyncDisposable
{
    private readonly Mock<IScheduledMessageStorage> _mockStorage;
    private readonly Mock<IMessageDeliveryHandler> _mockDeliveryHandler;
    private readonly Mock<ILogger<StorageBackedScheduler>> _mockLogger;
    private readonly StorageBackedSchedulerOptions _options;
    private StorageBackedScheduler? _sut;

    public StorageBackedSchedulerTests()
    {
        _mockStorage = new Mock<IScheduledMessageStorage>();
        _mockDeliveryHandler = new Mock<IMessageDeliveryHandler>();
        _mockLogger = new Mock<ILogger<StorageBackedScheduler>>();

        _options = new StorageBackedSchedulerOptions
        {
            PollingInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
            MaxConcurrency = 5,
            AutoCleanup = false // Disable auto-cleanup for tests
        };

        // Setup default mock behaviors
        _mockStorage.Setup(x => x.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _mockStorage.Setup(x => x.GetPendingCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sut != null)
        {
            await _sut.DisposeAsync();
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(null!, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System));

        Assert.Equal("storage", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullDeliveryHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_mockStorage.Object, null!, _options, _mockLogger.Object, TimeProvider.System));

        Assert.Equal("deliveryHandler", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, null!, _mockLogger.Object, TimeProvider.System));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, null!, TimeProvider.System));

        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, null!));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(_sut);
    }

    #endregion

    #region ScheduleAsync with Delay Tests

    [Fact]
    public async Task ScheduleAsync_WithDelay_SchedulesMessageForFutureDelivery()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var delay = TimeSpan.FromMinutes(5);
        ScheduledMessage? capturedMessage = null;

        _mockStorage.Setup(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledMessage, CancellationToken>((sm, ct) => capturedMessage = sm)
            .ReturnsAsync(new ScheduledMessageEntry());

        // Act
        var result = await _sut.ScheduleAsync(message, delay);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.True(result.ScheduledFor > DateTimeOffset.UtcNow);
        Assert.True(result.ScheduledFor <= DateTimeOffset.UtcNow.Add(delay).AddSeconds(1)); // Allow 1 second tolerance

        _mockStorage.Verify(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedMessage);
        Assert.Equal(message, capturedMessage.Message);
    }

    [Fact]
    public async Task ScheduleAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var delay = TimeSpan.FromMinutes(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _sut.ScheduleAsync<IMessage>(null!, delay));
    }

    [Fact]
    public async Task ScheduleAsync_WithNegativeDelay_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var delay = TimeSpan.FromMinutes(-1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _sut.ScheduleAsync(message, delay));

        Assert.Equal("delay", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public async Task ScheduleAsync_WithOptions_PassesOptionsToStorage()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var delay = TimeSpan.FromMinutes(5);
        var schedulingOptions = new SchedulingOptions { Priority = 10, Destination = "test-queue" };
        ScheduledMessage? capturedMessage = null;

        _mockStorage.Setup(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledMessage, CancellationToken>((sm, ct) => capturedMessage = sm)
            .ReturnsAsync(new ScheduledMessageEntry());

        // Act
        var result = await _sut.ScheduleAsync(message, delay, schedulingOptions);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedMessage);
        Assert.Equal(10, capturedMessage.Options.Priority);
        Assert.Equal("test-queue", capturedMessage.Options.Destination);
    }

    #endregion

    #region ScheduleAsync with DateTimeOffset Tests

    [Fact]
    public async Task ScheduleAsync_WithDateTimeOffset_SchedulesMessageForSpecificTime()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var deliverAt = DateTimeOffset.UtcNow.AddHours(1);
        ScheduledMessage? capturedMessage = null;

        _mockStorage.Setup(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ScheduledMessage, CancellationToken>((sm, ct) => capturedMessage = sm)
            .ReturnsAsync(new ScheduledMessageEntry());

        // Act
        var result = await _sut.ScheduleAsync(message, deliverAt);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.ScheduleId);
        Assert.Equal(deliverAt, result.ScheduledFor);

        _mockStorage.Verify(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedMessage);
        Assert.Equal(deliverAt, capturedMessage.DeliverAt);
    }

    [Fact]
    public async Task ScheduleAsync_WithPastDeliveryTime_ThrowsArgumentException()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var deliverAt = DateTimeOffset.UtcNow.AddHours(-1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _sut.ScheduleAsync(message, deliverAt));

        Assert.Equal("deliverAt", exception.ParamName);
        Assert.Contains("cannot be in the past", exception.Message);
    }

    [Fact]
    public async Task ScheduleAsync_WhenStorageThrows_ReturnsFailedResult()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var message = new Mock<IMessage>().Object;
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(5);

        _mockStorage.Setup(x => x.AddAsync(It.IsAny<ScheduledMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _sut.ScheduleAsync(message, deliverAt);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Storage error", result.ErrorMessage);
    }

    #endregion

    #region CancelScheduledAsync Tests

    [Fact]
    public async Task CancelScheduledAsync_WithValidScheduleId_ReturnsTrueWhenCancelled()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();

        _mockStorage.Setup(x => x.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.True(result);
        _mockStorage.Verify(x => x.CancelAsync(scheduleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelScheduledAsync_WithNonExistentScheduleId_ReturnsFalse()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();

        _mockStorage.Setup(x => x.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CancelScheduledAsync_WhenStorageThrows_ReturnsFalse()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();

        _mockStorage.Setup(x => x.CancelAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _sut.CancelScheduledAsync(scheduleId);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetScheduledAsync Tests

    [Fact]
    public async Task GetScheduledAsync_WithValidScheduleId_ReturnsMessageInfo()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();
        var message = new Mock<IMessage>();
        message.Setup(x => x.MessageId).Returns(Guid.NewGuid());

        var entry = new ScheduledMessageEntry
        {
            ScheduleId = scheduleId,
            Message = new ScheduledMessage
            {
                ScheduleId = scheduleId,
                Message = message.Object,
                DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5),
                ScheduledAt = DateTimeOffset.UtcNow,
                Options = new SchedulingOptions { Priority = 5 }
            },
            Status = ScheduledMessageStatus.Pending,
            DeliveredAt = null
        };

        _mockStorage.Setup(x => x.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _sut.GetScheduledAsync(scheduleId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(scheduleId, result.ScheduleId);
        Assert.Equal(message.Object.MessageId, result.MessageId);
        Assert.Equal(ScheduledMessageStatus.Pending, result.Status);
        Assert.Equal(5, result.Priority);
    }

    [Fact]
    public async Task GetScheduledAsync_WithNonExistentScheduleId_ReturnsNull()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();

        _mockStorage.Setup(x => x.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledMessageEntry?)null);

        // Act
        var result = await _sut.GetScheduledAsync(scheduleId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetScheduledAsync_WhenStorageThrows_ReturnsNull()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var scheduleId = Guid.NewGuid();

        _mockStorage.Setup(x => x.GetAsync(scheduleId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _sut.GetScheduledAsync(scheduleId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetPendingAsync Tests

    [Fact]
    public async Task GetPendingAsync_WithoutQuery_ReturnsPendingMessages()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var entries = new List<ScheduledMessageEntry>
        {
            CreateScheduledMessageEntry(Guid.NewGuid(), ScheduledMessageStatus.Pending),
            CreateScheduledMessageEntry(Guid.NewGuid(), ScheduledMessageStatus.Pending)
        };

        _mockStorage.Setup(x => x.QueryAsync(It.Is<ScheduledMessageQuery>(q => q.Status == ScheduledMessageStatus.Pending), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await _sut.GetPendingAsync();

        // Assert
        Assert.Equal(2, result.Count);
        _mockStorage.Verify(x => x.QueryAsync(It.IsAny<ScheduledMessageQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingAsync_WithQuery_UsesProvidedQuery()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var query = new ScheduledMessageQuery { Status = ScheduledMessageStatus.Delivered };
        var entries = new List<ScheduledMessageEntry>();

        _mockStorage.Setup(x => x.QueryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        // Act
        var result = await _sut.GetPendingAsync(query);

        // Assert
        Assert.Empty(result);
        _mockStorage.Verify(x => x.QueryAsync(query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingAsync_WhenStorageThrows_ReturnsEmptyList()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);

        _mockStorage.Setup(x => x.QueryAsync(It.IsAny<ScheduledMessageQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _sut.GetPendingAsync();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region GetPendingCountAsync Tests

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCountFromStorage()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);
        var expectedCount = 42L;

        _mockStorage.Setup(x => x.GetPendingCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCount);

        // Act
        var result = await _sut.GetPendingCountAsync();

        // Assert
        Assert.Equal(expectedCount, result);
        _mockStorage.Verify(x => x.GetPendingCountAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingCountAsync_WhenStorageThrows_ReturnsZero()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);

        _mockStorage.Setup(x => x.GetPendingCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        // Act
        var result = await _sut.GetPendingCountAsync();

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task DisposeAsync_StopsBackgroundWorkers()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);

        // Act
        await _sut.DisposeAsync();

        // Assert - Should complete without hanging or throwing
        Assert.True(true);
    }

    [Fact]
    public async Task DisposeAsync_MultipleTimes_CompletesWithoutError()
    {
        // Arrange
        _sut = new StorageBackedScheduler(_mockStorage.Object, _mockDeliveryHandler.Object, _options, _mockLogger.Object, TimeProvider.System);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Second call should complete without error

        // Assert
        Assert.True(true);
    }

    #endregion

    #region Helper Methods

    private ScheduledMessageEntry CreateScheduledMessageEntry(Guid scheduleId, ScheduledMessageStatus status)
    {
        var message = new Mock<IMessage>();
        message.Setup(x => x.MessageId).Returns(Guid.NewGuid());

        return new ScheduledMessageEntry
        {
            ScheduleId = scheduleId,
            Message = new ScheduledMessage
            {
                ScheduleId = scheduleId,
                Message = message.Object,
                DeliverAt = DateTimeOffset.UtcNow.AddMinutes(5),
                ScheduledAt = DateTimeOffset.UtcNow,
                Options = new SchedulingOptions()
            },
            Status = status,
            DeliveredAt = null
        };
    }

    #endregion
}
