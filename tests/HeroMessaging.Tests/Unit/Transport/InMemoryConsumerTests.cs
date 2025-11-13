using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

[Trait("Category", "Unit")]
public class InMemoryConsumerTests
{
    private readonly Mock<InMemoryTransport> _mockTransport;
    private readonly Mock<ITransportInstrumentation> _mockInstrumentation;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ConsumerOptions _options;
    private readonly TransportAddress _source;

    public InMemoryConsumerTests()
    {
        _mockTransport = new Mock<InMemoryTransport>(TimeProvider.System, null);
        _mockInstrumentation = new Mock<ITransportInstrumentation>();
        _timeProvider = new FakeTimeProvider();
        _source = new TransportAddress("test-queue", TransportAddressType.Queue);

        _options = new ConsumerOptions
        {
            ConcurrentMessageLimit = 5,
            AutoAcknowledge = true,
            MessageRetryPolicy = new MessageRetryPolicy
            {
                MaxAttempts = 3,
                BackoffType = BackoffType.Exponential,
                InitialDelay = TimeSpan.FromMilliseconds(100)
            }
        };

        // Setup default mock behaviors
        _mockInstrumentation.Setup(x => x.ExtractTraceContext(It.IsAny<TransportEnvelope>()))
            .Returns((TraceContext?)null);
        _mockInstrumentation.Setup(x => x.StartReceiveActivity(
                It.IsAny<TransportEnvelope>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TraceContext?>()))
            .Returns((System.Diagnostics.Activity?)null);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConsumerId_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryConsumer(
                null!,
                _source,
                handler,
                _options,
                _mockTransport.Object,
                _timeProvider,
                _mockInstrumentation.Object));

        Assert.Equal("consumerId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryConsumer(
                "consumer-1",
                _source,
                null!,
                _options,
                _mockTransport.Object,
                _timeProvider,
                _mockInstrumentation.Object));

        Assert.Equal("handler", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryConsumer(
                "consumer-1",
                _source,
                handler,
                null!,
                _mockTransport.Object,
                _timeProvider,
                _mockInstrumentation.Object));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryConsumer(
                "consumer-1",
                _source,
                handler,
                _options,
                null!,
                _timeProvider,
                _mockInstrumentation.Object));

        Assert.Equal("transport", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryConsumer(
                "consumer-1",
                _source,
                handler,
                _options,
                _mockTransport.Object,
                null!,
                _mockInstrumentation.Object));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        // Assert
        Assert.NotNull(consumer);
        Assert.Equal("consumer-1", consumer.ConsumerId);
        Assert.Equal(_source, consumer.Source);
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public void Constructor_WithNullInstrumentation_UsesNoOpInstrumentation()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        // Act
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            null);

        // Assert
        Assert.NotNull(consumer);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_SetsIsActiveToTrue()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        // Act
        await consumer.StartAsync();

        // Assert
        Assert.True(consumer.IsActive);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyStarted_DoesNotThrow()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        await consumer.StartAsync();

        // Act
        await consumer.StartAsync(); // Second start

        // Assert
        Assert.True(consumer.IsActive);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_SetsIsActiveToFalse()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        await consumer.StartAsync();

        // Act
        await consumer.StopAsync();

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_DoesNotThrow()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        // Act
        await consumer.StopAsync();

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task StopAsync_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        await consumer.StartAsync();

        // Act
        await consumer.StopAsync();
        await consumer.StopAsync(); // Second stop

        // Assert
        Assert.False(consumer.IsActive);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_InitialState_ReturnsZeroMetrics()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        // Act
        var metrics = consumer.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.MessagesReceived);
        Assert.Equal(0, metrics.MessagesProcessed);
        Assert.Equal(0, metrics.MessagesFailed);
        Assert.Equal(0, metrics.MessagesAcknowledged);
        Assert.Equal(0, metrics.MessagesRejected);
        Assert.Equal(0, metrics.MessagesDeadLettered);
        Assert.Equal(0, metrics.CurrentlyProcessing);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_StopsConsumer()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        await consumer.StartAsync();

        // Act
        await consumer.DisposeAsync();

        // Assert
        Assert.False(consumer.IsActive);
        _mockTransport.Verify(x => x.RemoveConsumer("consumer-1"), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenNotStarted_CompletesSuccessfully()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);
        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        // Act
        await consumer.DisposeAsync();

        // Assert
        Assert.False(consumer.IsActive);
    }

    #endregion

    #region Message Processing Tests

    [Fact]
    public async Task DeliverMessageAsync_WhenNotActive_DoesNotProcessMessage()
    {
        // Arrange
        var handlerCalled = false;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        });

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(100); // Give time for processing

        // Assert
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task ProcessMessage_WithAutoAcknowledge_CallsAcknowledge()
    {
        // Arrange
        var acknowledgeTask = new TaskCompletionSource<bool>();
        MessageContext? capturedContext = null;

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(async (e, c, ct) =>
        {
            capturedContext = c;
            await Task.CompletedTask;
        });

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options, // AutoAcknowledge is true
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        Assert.NotNull(capturedContext);
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesReceived);
        Assert.Equal(1, metrics.MessagesAcknowledged);
    }

    [Fact]
    public async Task ProcessMessage_WithoutAutoAcknowledge_DoesNotCallAcknowledge()
    {
        // Arrange
        MessageContext? capturedContext = null;

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(async (e, c, ct) =>
        {
            capturedContext = c;
            await Task.CompletedTask;
        });

        var optionsNoAck = new ConsumerOptions
        {
            ConcurrentMessageLimit = 5,
            AutoAcknowledge = false,
            MessageRetryPolicy = new MessageRetryPolicy { MaxAttempts = 1 }
        };

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            optionsNoAck,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        Assert.NotNull(capturedContext);
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesReceived);
        Assert.Equal(0, metrics.MessagesAcknowledged); // Should not auto-acknowledge
    }

    [Fact]
    public async Task ProcessMessage_WhenHandlerThrows_IncrementsFailedMetric()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) =>
        {
            throw new InvalidOperationException("Test exception");
        });

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(500); // Give time for processing and retries

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesFailed > 0);
    }

    [Fact]
    public async Task MessageContext_Acknowledge_IncrementsAcknowledgedMetric()
    {
        // Arrange
        MessageContext? capturedContext = null;

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(async (e, c, ct) =>
        {
            capturedContext = c;
            await c.AcknowledgeAsync(ct);
        });

        var optionsNoAck = new ConsumerOptions
        {
            ConcurrentMessageLimit = 5,
            AutoAcknowledge = false,
            MessageRetryPolicy = new MessageRetryPolicy { MaxAttempts = 1 }
        };

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            optionsNoAck,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesAcknowledged);
    }

    [Fact]
    public async Task MessageContext_Reject_IncrementsRejectedMetric()
    {
        // Arrange
        MessageContext? capturedContext = null;

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(async (e, c, ct) =>
        {
            capturedContext = c;
            await c.RejectAsync(false, ct);
        });

        var optionsNoAck = new ConsumerOptions
        {
            ConcurrentMessageLimit = 5,
            AutoAcknowledge = false,
            MessageRetryPolicy = new MessageRetryPolicy { MaxAttempts = 1 }
        };

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            optionsNoAck,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesRejected);
    }

    [Fact]
    public async Task MessageContext_DeadLetter_IncrementsDeadLetteredMetric()
    {
        // Arrange
        MessageContext? capturedContext = null;

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(async (e, c, ct) =>
        {
            capturedContext = c;
            await c.DeadLetterAsync("Test reason", ct);
        });

        var optionsNoAck = new ConsumerOptions
        {
            ConcurrentMessageLimit = 5,
            AutoAcknowledge = false,
            MessageRetryPolicy = new MessageRetryPolicy { MaxAttempts = 1 }
        };

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            optionsNoAck,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesDeadLettered);
    }

    #endregion

    #region Instrumentation Tests

    [Fact]
    public async Task ProcessMessage_WithInstrumentation_RecordsMetrics()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) => Task.CompletedTask);

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        _mockInstrumentation.Verify(x => x.ExtractTraceContext(envelope), Times.Once);
        _mockInstrumentation.Verify(x => x.RecordOperation(It.IsAny<string>(), "receive", "success"), Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_WhenFails_RecordsError()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>((e, c, ct) =>
        {
            throw new InvalidOperationException("Test error");
        });

        var consumer = new InMemoryConsumer(
            "consumer-1",
            _source,
            handler,
            _options,
            _mockTransport.Object,
            _timeProvider,
            _mockInstrumentation.Object);

        var envelope = new TransportEnvelope(
            Guid.NewGuid(),
            "TestMessage",
            new byte[] { 1, 2, 3 },
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            0);

        await consumer.StartAsync();

        // Act
        await consumer.DeliverMessageAsync(envelope);
        await Task.Delay(600); // Give time for processing and retries

        // Assert
        _mockInstrumentation.Verify(x => x.RecordError(It.IsAny<System.Diagnostics.Activity?>(), It.IsAny<Exception>()), Times.AtLeastOnce);
        _mockInstrumentation.Verify(x => x.RecordOperation(It.IsAny<string>(), "receive", "failure"), Times.AtLeastOnce);
    }

    #endregion
}
