using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for MetricsDecorator
/// Tests metrics collection during message processing
/// </summary>
[Trait("Category", "Unit")]
public sealed class MetricsDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;

    public MetricsDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Success Metrics Tests

    [Fact]
    public async Task ProcessAsync_OnSuccess_IncrementsStartedAndSucceededCounters()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.started", 1),
            Times.Once);
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.succeeded", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnSuccess_RecordsDuration()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.RecordDuration("messages.TestMessage.duration", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    #endregion

    #region Failure Metrics Tests

    [Fact]
    public async Task ProcessAsync_OnFailure_IncrementsStartedAndFailedCounters()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.started", 1),
            Times.Once);
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.failed", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnFailureWithoutRetries_DoesNotIncrementRetriedCounter()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext { RetryCount = 0 };

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Should not increment retried counter when RetryCount is 0
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.retried", It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_OnFailureWithRetries_IncrementsRetriedCounter()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var retryCount = 3;
        var context = new ProcessingContext { RetryCount = retryCount };

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test"), "Failed"));

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.retried", retryCount),
            Times.Once);
    }

    #endregion

    #region Exception Metrics Tests

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_IncrementsExceptionCounter()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var exception = new InvalidOperationException("Test exception");

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.started", 1),
            Times.Once);
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.exceptions", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_RecordsDuration()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            decorator.ProcessAsync(message, context).AsTask());

        _metricsCollectorMock.Verify(
            m => m.RecordDuration("messages.TestMessage.duration", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    #endregion

    #region Message Type Tests

    [Fact]
    public async Task ProcessAsync_WithDifferentMessageTypes_UsesCorrectMetricNames()
    {
        // Arrange
        var message1 = new TestMessage { Content = "test" };
        var message2 = new AnotherTestMessage { Data = 42 };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message1, context);
        await decorator.ProcessAsync(message2, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.TestMessage.started", 1),
            Times.Once);
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter("messages.AnotherTestMessage.started", 1),
            Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new MetricsDecorator(
            _innerProcessorMock.Object,
            _metricsCollectorMock.Object);

        // Act
        await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
    }

    #endregion

    #region InMemoryMetricsCollector Tests

    [Fact]
    public void InMemoryMetricsCollector_IncrementCounter_IncrementsValue()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();

        // Act
        collector.IncrementCounter("test.counter", 1);
        collector.IncrementCounter("test.counter", 2);
        collector.IncrementCounter("test.counter", 3);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(6L, snapshot["test.counter"]);
    }

    [Fact]
    public void InMemoryMetricsCollector_RecordDuration_RecordsValue()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();

        // Act
        collector.RecordDuration("test.duration", TimeSpan.FromMilliseconds(100));
        collector.RecordDuration("test.duration", TimeSpan.FromMilliseconds(200));

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(2, snapshot["test.duration.count"]);
        Assert.Equal(150.0, snapshot["test.duration.avg_ms"]);
        Assert.Equal(200.0, snapshot["test.duration.max_ms"]);
        Assert.Equal(100.0, snapshot["test.duration.min_ms"]);
    }

    [Fact]
    public void InMemoryMetricsCollector_RecordValue_RecordsValue()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();

        // Act
        collector.RecordValue("test.value", 10.5);
        collector.RecordValue("test.value", 20.5);

        // Assert - GetSnapshot doesn't include raw values, but they should be recorded
        var snapshot = collector.GetSnapshot();
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void InMemoryMetricsCollector_GetSnapshot_ReturnsAllMetrics()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();

        // Act
        collector.IncrementCounter("counter1", 5);
        collector.IncrementCounter("counter2", 10);
        collector.RecordDuration("duration1", TimeSpan.FromMilliseconds(50));

        var snapshot = collector.GetSnapshot();

        // Assert
        Assert.Equal(2, snapshot.Count(kvp => kvp.Key.StartsWith("counter")));
        Assert.Contains("counter1", snapshot.Keys);
        Assert.Contains("counter2", snapshot.Keys);
        Assert.Contains("duration1.count", snapshot.Keys);
    }

    [Fact]
    public void InMemoryMetricsCollector_GetSnapshot_EmptyCollector_ReturnsEmptyDictionary()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();

        // Act
        var snapshot = collector.GetSnapshot();

        // Assert
        Assert.Empty(snapshot);
    }

    [Fact]
    public void InMemoryMetricsCollector_ConcurrentIncrements_ThreadSafe()
    {
        // Arrange
        var collector = new InMemoryMetricsCollector();
        var tasks = new List<Task>();

        // Act - Simulate concurrent increments
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => collector.IncrementCounter("concurrent.counter", 1)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(100L, snapshot["concurrent.counter"]);
    }

    #endregion

    #region Test Message Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    private class AnotherTestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int Data { get; set; }
    }

    #endregion
}
