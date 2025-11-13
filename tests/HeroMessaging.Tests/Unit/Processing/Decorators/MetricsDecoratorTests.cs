using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class MetricsDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;

    public MetricsDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();
    }

    private MetricsDecorator CreateDecorator()
    {
        return new MetricsDecorator(_innerMock.Object, _metricsCollectorMock.Object);
    }

    #region ProcessAsync - Success Cases

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_IncrementsStartedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.started", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_IncrementsSucceededCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.succeeded", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_RecordsDuration()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.RecordDuration($"messages.{nameof(TestMessage)}.duration", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithSuccessfulResult_ReturnsResultFromInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedData = new { Value = 42 };

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful("Success", expectedData));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Success", result.Message);
        Assert.Equal(expectedData, result.Data);
    }

    #endregion

    #region ProcessAsync - Failure Cases

    [Fact]
    public async Task ProcessAsync_WithFailedResult_IncrementsStartedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.started", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_IncrementsFailedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.failed", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_DoesNotIncrementSucceededCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.succeeded", It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_ReturnsFailureResult()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException, "Failure message"));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(testException, result.Exception);
        Assert.Equal("Failure message", result.Message);
    }

    #endregion

    #region ProcessAsync - Retry Metrics

    [Fact]
    public async Task ProcessAsync_WithRetryCount_IncrementsRetriedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext().WithRetry(2);

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.retried", 2),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithZeroRetryCount_DoesNotIncrementRetriedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Test error")));

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.retried", It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithRetryCountAndSuccess_DoesNotIncrementRetriedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext().WithRetry(2);

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.retried", It.IsAny<int>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync - Exception Cases

    [Fact]
    public async Task ProcessAsync_WithException_IncrementsStartedCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.started", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithException_IncrementsExceptionsCounter()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(TestMessage)}.exceptions", 1),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithException_RecordsDuration()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _metricsCollectorMock.Verify(
            m => m.RecordDuration($"messages.{nameof(TestMessage)}.duration", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithException_RethrowsException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test exception");

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(testException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        Assert.Equal("Test exception", exception.Message);
    }

    #endregion

    #region ProcessAsync - Duration Recording

    [Fact]
    public async Task ProcessAsync_RecordsDurationForSlowOperation()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        TimeSpan? recordedDuration = null;

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(50); // Simulate processing time
                return ProcessingResult.Successful();
            });

        _metricsCollectorMock
            .Setup(m => m.RecordDuration(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Callback<string, TimeSpan>((name, duration) => recordedDuration = duration);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(recordedDuration);
        Assert.True(recordedDuration.Value.TotalMilliseconds >= 50,
            $"Expected duration >= 50ms, but was {recordedDuration.Value.TotalMilliseconds}ms");
    }

    #endregion

    #region ProcessAsync - Message Type Metrics

    [Fact]
    public async Task ProcessAsync_UsesMessageTypeInMetricNames()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new SpecialTestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(SpecialTestMessage)}.started", 1),
            Times.Once);
        _metricsCollectorMock.Verify(
            m => m.IncrementCounter($"messages.{nameof(SpecialTestMessage)}.succeeded", 1),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Cancellation

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, cancellationToken))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _innerMock.Verify(p => p.ProcessAsync(message, context, cancellationToken), Times.Once);
    }

    #endregion

    #region Test Helper Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class SpecialTestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
