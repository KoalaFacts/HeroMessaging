using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Choreography;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public sealed class CorrelationContextDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<ILogger<CorrelationContextDecorator>> _loggerMock;

    public CorrelationContextDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<CorrelationContextDecorator>>();
    }

    private CorrelationContextDecorator CreateDecorator()
    {
        return new CorrelationContextDecorator(_innerMock.Object, _loggerMock.Object);
    }

    #region ProcessAsync - Correlation Context Setup

    [Fact]
    public async Task ProcessAsync_SetsUpCorrelationContext()
    {
        // Arrange
        var decorator = CreateDecorator();
        var correlationId = "correlation-123";
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();

        string? capturedCorrelationId = null;
        string? capturedMessageId = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedCorrelationId = CorrelationContext.CurrentCorrelationId;
                capturedMessageId = CorrelationContext.CurrentMessageId;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(correlationId, capturedCorrelationId);
        Assert.Equal(message.MessageId.ToString(), capturedMessageId);
    }

    [Fact]
    public async Task ProcessAsync_WithNullCorrelationId_UsesMessageIdAsCorrelationId()
    {
        // Arrange
        var decorator = CreateDecorator();
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = null
        };
        var context = new ProcessingContext();

        string? capturedCorrelationId = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedCorrelationId = CorrelationContext.CurrentCorrelationId;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.Equal(messageId.ToString(), capturedCorrelationId);
    }

    [Fact]
    public async Task ProcessAsync_ClearsCorrelationContextAfterProcessing()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "correlation-123"
        };
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert - Context should be cleared after processing
        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    public async Task ProcessAsync_ClearsCorrelationContextEvenOnException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "correlation-123"
        };
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        // Assert - Context should be cleared even after exception
        Assert.Null(CorrelationContext.Current);
    }

    #endregion

    #region ProcessAsync - Context Enrichment

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithCorrelationId()
    {
        // Arrange
        var decorator = CreateDecorator();
        var correlationId = "correlation-456";
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = correlationId
        };
        var context = new ProcessingContext();

        ProcessingContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        var storedCorrelationId = capturedContext.Value.GetMetadataReference<string>("CorrelationId");
        Assert.Equal(correlationId, storedCorrelationId);
    }

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithCausationId()
    {
        // Arrange
        var decorator = CreateDecorator();
        var causationId = "causation-789";
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = causationId
        };
        var context = new ProcessingContext();

        ProcessingContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        var storedCausationId = capturedContext.Value.GetMetadataReference<string>("CausationId");
        Assert.Equal(causationId, storedCausationId);
    }

    [Fact]
    public async Task ProcessAsync_EnrichesContextWithMessageId()
    {
        // Arrange
        var decorator = CreateDecorator();
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId
        };
        var context = new ProcessingContext();

        ProcessingContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        var storedMessageId = capturedContext.Value.GetMetadataReference<string>("MessageId");
        Assert.Equal(messageId.ToString(), storedMessageId);
    }

    [Fact]
    public async Task ProcessAsync_WithNullCausationId_StoresEmptyString()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = null
        };
        var context = new ProcessingContext();

        ProcessingContext? capturedContext = null;

        _innerMock
            .Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Returns<IMessage, ProcessingContext, CancellationToken>((msg, ctx, ct) =>
            {
                capturedContext = ctx;
                return ValueTask.FromResult(ProcessingResult.Successful());
            });

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        var storedCausationId = capturedContext.Value.GetMetadataReference<string>("CausationId");
        Assert.Equal(string.Empty, storedCausationId);
    }

    #endregion

    #region ProcessAsync - Inner Processor Invocation

    [Fact]
    public async Task ProcessAsync_CallsInnerProcessorWithEnrichedContext()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsResultFromInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful("Test message");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test message", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithFailedResult_ReturnsFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var testException = new InvalidOperationException("Test error");

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(testException));

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(testException, result.Exception);
    }

    #endregion

    #region ProcessAsync - Logging

    [Fact]
    public async Task ProcessAsync_LogsDebugWithCorrelationInformation()
    {
        // Arrange
        var decorator = CreateDecorator();
        var correlationId = "correlation-999";
        var causationId = "causation-888";
        var messageId = Guid.NewGuid();
        var message = new TestMessage
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = causationId
        };
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithTraceEnabled_LogsTraceOnCompletion()
    {
        // Arrange
        _loggerMock
            .Setup(l => l.IsEnabled(LogLevel.Trace))
            .Returns(true);

        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithTraceDisabled_DoesNotLogTrace()
    {
        // Arrange
        _loggerMock
            .Setup(l => l.IsEnabled(LogLevel.Trace))
            .Returns(false);

        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _innerMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Trace,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
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
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), cancellationToken))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _innerMock.Verify(
            p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), cancellationToken),
            Times.Once);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
