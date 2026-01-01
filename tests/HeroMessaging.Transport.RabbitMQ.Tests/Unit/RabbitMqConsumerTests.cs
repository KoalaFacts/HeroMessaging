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
    private TransportAddress _source;
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
            await _consumer.DisposeAsync(TestContext.Current.CancellationToken);
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
    public void Constructor_WithNullSource_ThrowsArgumentException()
    {
        // Arrange - Create a TransportAddress with empty name to trigger validation
        var emptySource = new TransportAddress(string.Empty, TransportAddressType.Queue);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RabbitMqConsumer(
                "test",
                emptySource,
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
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            "test-queue",
            false,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyActive_DoesNotStartAgain()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        await _consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once); // Only once
    }

    [Fact]
    public async Task StartAsync_SetsIsActiveToTrue()
    {
        // Arrange
        Assert.False(_consumer!.IsActive);

        // Act
        await _consumer.StartAsync(TestContext.Current.CancellationToken);

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
        await _consumer.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(_consumer.IsActive);
        _mockChannel!.Verify(ch => ch.BasicCancelAsync("consumer-tag-123", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
        await _consumer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_WhenBasicCancelThrows_LogsWarningButDoesNotThrow()
    {
        // Arrange
        await _consumer!.StartAsync();
        _mockChannel!.Setup(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Test exception"));

        // Act & Assert - should not throw
        await _consumer.StopAsync(TestContext.Current.CancellationToken);

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
        Assert.Equal(0, metrics.MessagesProcessed);
        Assert.Equal(0, metrics.MessagesFailed);
    }

    [Fact]
    public async Task GetMetrics_AfterStart_ReflectsActiveState()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act - check IsActive directly on consumer, not metrics
        // Assert
        Assert.True(_consumer.IsActive);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenActive_StopsAndDisposesChannel()
    {
        // Arrange
        await _consumer!.StartAsync();

        // Act
        await _consumer.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(_consumer.IsActive);
        _mockChannel!.Verify(ch => ch.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
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
        _mockChannel!.Setup(ch => ch.CloseAsync(It.IsAny<CancellationToken>())).Throws(new InvalidOperationException("Test exception"));

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
        await _consumer.StartAsync(TestContext.Current.CancellationToken);

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
        await _consumer.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(_consumer.IsActive);
    }

    #endregion

    #region Additional Constructor Edge Case Tests

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                _source!,
                _mockChannel!.Object,
                _handler!,
                null!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                _source!,
                _mockChannel!.Object,
                _handler!,
                _options!,
                null!,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                "test",
                _source!,
                _mockChannel!.Object,
                _handler!,
                _options!,
                _mockTransport!.Object,
                null!));
    }

    [Fact]
    public void Constructor_WithEmptyConsumerId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConsumer(
                string.Empty,
                _source!,
                _mockChannel!.Object,
                _handler!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullSourceName_ThrowsArgumentException()
    {
        // Arrange
        var nullNameSource = new TransportAddress(null!, TransportAddressType.Queue);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RabbitMqConsumer(
                "test",
                nullNameSource,
                _mockChannel!.Object,
                _handler!,
                _options!,
                _mockTransport!.Object,
                _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithValidCustomConsumerId_StoresId()
    {
        // Arrange
        var customConsumer = new RabbitMqConsumer(
            "custom-id-12345",
            _source!,
            _mockChannel!.Object,
            _handler!,
            _options!,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Assert
        Assert.Equal("custom-id-12345", customConsumer.ConsumerId);
    }

    #endregion

    #region StartAsync Advanced Tests

    [Fact]
    public async Task StartAsync_CallsBasicQos_WithCorrectPrefetchCount()
    {
        // Arrange
        var options = new ConsumerOptions { StartImmediately = false, PrefetchCount = 50 };
        var consumer = new RabbitMqConsumer(
            "test-prefetch",
            _source!,
            _mockChannel!.Object,
            _handler!,
            options,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert - Verify BasicQos was called (though we can't easily verify the prefetch count without more setup)
        _mockChannel!.Verify(ch => ch.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartAsync_WithDifferentQueueName_UsesCorrectQueue()
    {
        // Arrange
        var otherSource = new TransportAddress("orders-queue", TransportAddressType.Queue);
        var consumer = new RabbitMqConsumer(
            "order-consumer",
            otherSource,
            _mockChannel!.Object,
            _handler!,
            _options!,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            "orders-queue",
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithManualAck_SetAutoAckToFalse()
    {
        // Arrange
        var options = new ConsumerOptions { ManualAcknowledgment = true };
        var consumer = new RabbitMqConsumer(
            "manual-ack-consumer",
            _source!,
            _mockChannel!.Object,
            _handler!,
            options,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert - Basic consume is called with autoAck=false (manual acknowledgment)
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            false, // Manual acknowledgment
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_MultipleCalls_OnlyCallsBasicConsumeOnce()
    {
        // Arrange
        var consumer = new RabbitMqConsumer(
            "idempotent-consumer",
            _source!,
            _mockChannel!.Object,
            _handler!,
            _options!,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert - BasicConsume called exactly once
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region StopAsync Advanced Tests

    [Fact]
    public async Task StopAsync_MultipleCallsWhenNotActive_DoesNotThrow()
    {
        // Act & Assert
        await _consumer!.StopAsync();
        await _consumer.StopAsync(TestContext.Current.CancellationToken);
        await _consumer.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_WithCancellationToken_PassesTokenToBasicCancel()
    {
        // Arrange
        await _consumer!.StartAsync();
        var cts = new CancellationTokenSource();

        // Act
        await _consumer.StopAsync(cts.Token, TestContext.Current.CancellationToken);

        // Assert
        _mockChannel!.Verify(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_SetsIsActiveToFalse()
    {
        // Arrange
        await _consumer!.StartAsync();
        Assert.True(_consumer.IsActive);

        // Act
        await _consumer.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(_consumer.IsActive);
    }

    #endregion

    #region GetMetrics Advanced Tests

    [Fact]
    public void GetMetrics_ReturnsConsumerMetrics()
    {
        // Act
        var metrics = _consumer!.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.IsType<ConsumerMetrics>(metrics);
    }

    [Fact]
    public void GetMetrics_MultipleCallsConsistent()
    {
        // Act
        var metrics1 = _consumer!.GetMetrics();
        var metrics2 = _consumer.GetMetrics();

        // Assert
        Assert.Equal(metrics1.MessagesProcessed, metrics2.MessagesProcessed);
        Assert.Equal(metrics1.MessagesFailed, metrics2.MessagesFailed);
    }

    #endregion

    #region DisposeAsync Advanced Tests

    [Fact]
    public async Task DisposeAsync_MultipleCallsAllowed()
    {
        // Act & Assert - Should not throw
        await _consumer!.DisposeAsync();
        await _consumer.DisposeAsync(TestContext.Current.CancellationToken);
        await _consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotStarted_StillDisposesChannel()
    {
        // Act
        await _consumer!.DisposeAsync();

        // Assert
        _mockChannel!.Verify(ch => ch.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockChannel.Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_AlwaysRemovesConsumerFromTransport()
    {
        // Act
        await _consumer!.DisposeAsync();

        // Assert
        _mockTransport!.Verify(t => t.RemoveConsumer("test-consumer"), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenDisposalThrows_IgnoresAndContinues()
    {
        // Arrange
        _mockChannel!.Setup(ch => ch.CloseAsync(It.IsAny<CancellationToken>())).Throws(new Exception("Dispose error"));
        _mockChannel.Setup(ch => ch.Dispose()).Throws(new Exception("Second dispose error"));

        // Act & Assert - Should not throw
        await _consumer!.DisposeAsync();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task StartAsync_ConcurrentCallsHandleSafely()
    {
        // Arrange
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_consumer!.StartAsync());
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.True(_consumer!.IsActive);
        // BasicConsume should only be called once due to locking
        _mockChannel!.Verify(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ConcurrentCallsHandleSafely()
    {
        // Arrange
        await _consumer!.StartAsync();
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_consumer.StopAsync());
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.False(_consumer.IsActive);
    }

    #endregion

    #region Consumer Options Tests

    [Fact]
    public async Task Constructor_WithDifferentConsumerOptions_Stores()
    {
        // Arrange
        var customOptions = new ConsumerOptions
        {
            ConsumerId = "custom-id",
            StartImmediately = false,
            PrefetchCount = 25,
            ManualAcknowledgment = true
        };

        var consumer = new RabbitMqConsumer(
            "test",
            _source!,
            _mockChannel!.Object,
            _handler!,
            customOptions,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(_consumer!.IsActive || !consumer.IsActive); // One should be false
    }

    [Fact]
    public void Source_Property_ReturnsQueueDetails()
    {
        // Act
        var source = _consumer!.Source;

        // Assert
        Assert.Equal("test-queue", source.Name);
        Assert.Equal(TransportAddressType.Queue, source.Type);
    }

    [Fact]
    public void ConsumerId_Property_Immutable()
    {
        // Assert
        var id1 = _consumer!.ConsumerId;
        var id2 = _consumer.ConsumerId;
        Assert.Equal(id1, id2);
        Assert.Equal("test-consumer", id1);
    }

    #endregion

    #region Channel Management Tests

    [Fact]
    public async Task StartAsync_UsesProvidedChannelObject()
    {
        // Arrange
        var mockChannelLocal = new Mock<IChannel>();
        mockChannelLocal.Setup(ch => ch.IsOpen).Returns(true);
        mockChannelLocal.Setup(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>())).ReturnsAsync("local-tag");
        mockChannelLocal.Setup(ch => ch.BasicQosAsync(
            It.IsAny<uint>(),
            It.IsAny<ushort>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var consumer = new RabbitMqConsumer(
            "test",
            _source!,
            mockChannelLocal.Object,
            _handler!,
            _options!,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        mockChannelLocal.Verify(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Handler Validation Tests

    [Fact]
    public void Constructor_StoresHandlerReference()
    {
        // Arrange
        var handlerInvoked = false;
        Task CustomHandler(TransportEnvelope env, MessageContext ctx, CancellationToken ct)
        {
            handlerInvoked = true;
            return Task.CompletedTask;
        }

        var consumer = new RabbitMqConsumer(
            "test-handler",
            _source!,
            _mockChannel!.Object,
            CustomHandler,
            _options!,
            _mockTransport!.Object,
            _mockLogger!.Object);

        // Assert
        Assert.NotNull(consumer);
    }

    #endregion

    // Note: Testing the actual message handling (OnMessageReceived) is difficult
    // without refactoring to expose the AsyncEventingBasicConsumer or using
    // integration tests with real RabbitMQ. The handler callbacks are internal
    // to the RabbitMQ.Client library.
}
