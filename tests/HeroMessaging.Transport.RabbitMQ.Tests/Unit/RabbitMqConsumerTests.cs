using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// Unit tests for RabbitMqConsumer
/// Target: 100% coverage for consumer lifecycle and message handling
/// </summary>
[Trait("Category", "Unit")]
public class RabbitMqConsumerTests : IAsyncLifetime
{
    private Mock<IChannel>? _mockChannel;
    private Mock<RabbitMqTransport>? _mockTransport;
    private Mock<ILogger<RabbitMqConsumer>>? _mockLogger;
    private Func<TransportEnvelope, MessageContext, CancellationToken, Task>? _handler;
    private TransportAddress? _source;
    private ConsumerOptions? _options;
    private RabbitMqConsumer? _consumer;
    private List<(TransportEnvelope envelope, MessageContext context)> _handledMessages;

    public ValueTask InitializeAsync()
    {
        _mockChannel = new Mock<IChannel>();
        _mockLogger = new Mock<ILogger<RabbitMqConsumer>>();
        _handledMessages = new List<(TransportEnvelope, MessageContext)>();
        _mockChannel.Setup(ch => ch.IsOpen).Returns(true);
        _mockChannel.Setup(ch => ch.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockChannel.Setup(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync("consumer-tag-123");

        _mockChannel.Setup(ch => ch.BasicCancelAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()
        )).Returns(Task.CompletedTask);

        _mockChannel.Setup(ch => ch.BasicQosAsync(
            It.IsAny<uint>(),
            It.IsAny<ushort>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()
        )).Returns(Task.CompletedTask);

        // Setup transport - create a real instance for testing
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var transportOptions = new RabbitMqTransportOptions
        {
            Host = "localhost"
        };

        _mockTransport = new Mock<RabbitMqTransport>(transportOptions, mockLoggerFactory.Object);

        _source = new TransportAddress("test-queue", TransportAddressType.Queue);
        _options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            StartImmediately = false
        };

        _handler = (envelope, context, ct) =>
        {
            _handledMessages.Add((envelope, context));
            return Task.CompletedTask;
        };

        _consumer = new RabbitMqConsumer(
            "test-consumer",
            _source,
            _mockChannel.Object,
            _handler,
            _options,
            _mockTransport.Object,
            _mockLogger.Object);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_consumer != null)
        {
            await _consumer.DisposeAsync();
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_InitializesSuccessfully()
    {
        // Assert
        Assert.NotNull(_consumer);
        Assert.Equal("test-consumer", _consumer!.ConsumerId);
        Assert.Equal(_source, _consumer.Source);
        Assert.False(_consumer.IsActive);
    }

    [Fact]
    public void Constructor_WithNullConsumerId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                null!,
                _source!,
                _mockChannel!.Object,
                _handler!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullSource_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                null!,
                _mockChannel!.Object,
                _handler!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullChannel_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                _source!,
                null!,
                _handler!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                _source!,
                _mockChannel!.Object,
                null!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenNotActive_StartsConsuming()
    {
        // Act
        await _consumer!.StartAsync();

        // Assert
        Assert.True(_consumer.IsActive);
        _mockChannel!.Verify(ch => ch.BasicConsume(
            "test-queue",
            false, // autoAck
            It.IsAny<IBasicConsumer>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyActive_DoesNotStartAgain()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        await _consumer.StartAsync();

        // Assert
        _mockChannel!.Verify(ch => ch.BasicConsume(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<IBasicConsumer>()), Times.Once); // Only once
    }

    [Fact]
    public async Task StartAsync_SetsIsActiveToTrue()
    {
        // Arrange
        Assert.False(_consumer!.IsActive);

        // Act
        await _consumer.StartAsync();

        // Assert
        Assert.True(_consumer.IsActive);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WhenActive_StopsConsuming()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        await _consumer.StopAsync();

        // Assert
        Assert.False(_consumer.IsActive);
        _mockChannel!.Verify(ch => ch.BasicCancel("consumer-tag-123"), Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenNotActive_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _consumer!.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenChannelClosed_DoesNotThrow()
    {
        // Arrange
        await _consumer!.StartAsync();
        _mockChannel!.Setup(ch => ch.IsOpen).Returns(false);

        // Act & Assert - should not throw
        await _consumer.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenBasicCancelThrows_LogsWarningButDoesNotThrow()
    {
        // Arrange
        await _consumer!.StartAsync();
        _mockChannel!.Setup(ch => ch.BasicCancel(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Test exception"));

        // Act & Assert - should not throw
        await _consumer.StopAsync();

        // Verify warning was logged
        _mockLogger!.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error cancelling consumer")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_InitialState_ReturnsCorrectMetrics()
    {
        // Act
        var metrics = _consumer!.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal("test-consumer", metrics.ConsumerId);
        Assert.Equal("test-queue", metrics.Source);
        Assert.False(metrics.IsActive);
        Assert.Equal(0, metrics.MessagesProcessed);
        Assert.Equal(0, metrics.MessagesFailed);
    }

    [Fact]
    public async Task GetMetrics_AfterStart_ReflectsActiveState()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        var metrics = _consumer.GetMetrics();

        // Assert
        Assert.True(metrics.IsActive);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenActive_StopsAndDisposesChannel()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        await _consumer.DisposeAsync();

        // Assert
        Assert.False(_consumer.IsActive);
        _mockChannel!.Verify(ch => ch.Close(), Times.Once);
        _mockChannel.Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_RemovesConsumerFromTransport()
    {
        // Act
        await _consumer!.DisposeAsync();

        // Assert
        _mockTransport!.Verify(t => t.RemoveConsumer("test-consumer"), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenChannelCloseThrows_LogsWarningButCompletes()
    {
        // Arrange
        _mockChannel!.Setup(ch => ch.Close()).Throws(new InvalidOperationException("Test exception"));

        // Act & Assert - should not throw
        await _consumer!.DisposeAsync();

        // Verify warning was logged
        _mockLogger!.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error disposing channel")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void ConsumerId_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal("test-consumer", _consumer!.ConsumerId);
    }

    [Fact]
    public void Source_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(_source, _consumer!.Source);
        Assert.Equal("test-queue", _consumer.Source.Name);
        Assert.Equal(TransportAddressType.Queue, _consumer.Source.Type);
    }

    [Fact]
    public void IsActive_InitiallyFalse()
    {
        // Assert
        Assert.False(_consumer!.IsActive);
    }

    [Fact]
    public async Task IsActive_AfterStart_BecomesTrue()
    {
        // Arrange
        Assert.False(_consumer!.IsActive);

        // Act
        await _consumer.StartAsync();

        // Assert
        Assert.True(_consumer.IsActive);
    }

    [Fact]
    public async Task IsActive_AfterStop_BecomesFalse()
    {
        // Arrange
        await _consumer!.StartAsync();
        Assert.True(_consumer.IsActive);

        // Act
        await _consumer.StopAsync();

        // Assert
        Assert.False(_consumer.IsActive);
    }

    #endregion

    // Note: Testing the actual message handling (OnMessageReceived) is difficult
    // without refactoring to expose the AsyncEventingBasicConsumer or using
    // integration tests with real RabbitMQ. The handler callbacks are internal
    // to the RabbitMQ.Client library.
}
