using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Choreography;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for CorrelationContextDecorator
/// Tests correlation context setup and propagation
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorrelationContextDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<ILogger<CorrelationContextDecorator>> _loggerMock;

    public CorrelationContextDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<CorrelationContextDecorator>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Context Enrichment Tests

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithCorrelationId()
    {
        // Arrange
        var correlationId = "correlation-123";
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.True(capturedContext.Value.Metadata.ContainsKey("CorrelationId"));
        Assert.Equal(correlationId, capturedContext.Value.Metadata["CorrelationId"]);
    }

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithCausationId()
    {
        // Arrange
        var causationId = "causation-456";
        var message = new TestMessage
        {
            Content = "test",
            CausationId = causationId
        };
        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.True(capturedContext.Value.Metadata.ContainsKey("CausationId"));
        Assert.Equal(causationId, capturedContext.Value.Metadata["CausationId"]);
    }

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithMessageId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage { Content = "test" };
        typeof(TestMessage).GetProperty(nameof(IMessage.MessageId))!.SetValue(message, messageId);

        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.True(capturedContext.Value.Metadata.ContainsKey("MessageId"));
        Assert.Equal(messageId.ToString(), capturedContext.Value.Metadata["MessageId"]);
    }

    [Fact]
    public async Task ProcessAsync_WhenCorrelationIdIsNull_UsesMessageId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = null
        };
        typeof(TestMessage).GetProperty(nameof(IMessage.MessageId))!.SetValue(message, messageId);

        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(messageId.ToString(), capturedContext.Value.Metadata["CorrelationId"]);
    }

    [Fact]
    public async Task ProcessAsync_WhenCausationIdIsNull_UsesEmptyString()
    {
        // Arrange
        var message = new TestMessage
        {
            Content = "test",
            CausationId = null
        };
        var context = new ProcessingContext();
        ProcessingContext? capturedContext = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(string.Empty, capturedContext.Value.Metadata["CausationId"]);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ProcessAsync_LogsDebugWithCorrelationInfo()
    {
        // Arrange
        var correlationId = "correlation-789";
        var causationId = "causation-012";
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = correlationId,
            CausationId = causationId
        };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Processing message") &&
                    v.ToString()!.Contains(correlationId) &&
                    v.ToString()!.Contains(causationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenTraceLevelEnabled_LogsCompletion()
    {
        // Arrange
        var correlationId = "correlation-trace";
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();

        _loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(true);

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(l => l.IsEnabled(LogLevel.Trace), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains("Completed processing") &&
                    v.ToString()!.Contains(correlationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenTraceLevelDisabled_DoesNotLogCompletion()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(l => l.IsEnabled(LogLevel.Trace), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region Processing Tests

    [Fact]
    public async Task ProcessAsync_CallsInnerProcessorWithEnrichedContext()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _innerProcessorMock.Verify(
            p => p.ProcessAsync(
                message,
                It.Is<ProcessingContext>(ctx =>
                    ctx.Metadata.ContainsKey("CorrelationId") &&
                    ctx.Metadata.ContainsKey("CausationId") &&
                    ctx.Metadata.ContainsKey("MessageId")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsResultFromInnerProcessor()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(expectedResult.Success, result.Success);
    }

    #endregion

    #region Correlation Scope Tests

    [Fact]
    public async Task ProcessAsync_CreatesCorrelationScope()
    {
        // Arrange
        var correlationId = "scope-test-correlation";
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();
        string? capturedCorrelationId = null;

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                // Capture correlation context during processing
                capturedCorrelationId = CorrelationContext.Current?.CorrelationId;
            })
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(correlationId, capturedCorrelationId);
    }

    [Fact]
    public async Task ProcessAsync_DisposesCorrelationScopeAfterProcessing()
    {
        // Arrange
        var correlationId = "dispose-test";
        var message = new TestMessage
        {
            Content = "test",
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Correlation context should be cleared after processing
        Assert.Null(CorrelationContext.Current);
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

        _innerProcessorMock.Setup(p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                cts.Token))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new CorrelationContextDecorator(
            _innerProcessorMock.Object,
            _loggerMock.Object);

        // Act
        await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        _innerProcessorMock.Verify(
            p => p.ProcessAsync(
                It.IsAny<IMessage>(),
                It.IsAny<ProcessingContext>(),
                cts.Token),
            Times.Once);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
