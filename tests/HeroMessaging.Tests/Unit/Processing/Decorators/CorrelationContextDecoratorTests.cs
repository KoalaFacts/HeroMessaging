using System.Collections.Immutable;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Choreography;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public abstract class CorrelationContextDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<ILogger<CorrelationContextDecorator>> _loggerMock;

    protected CorrelationContextDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _loggerMock = new Mock<ILogger<CorrelationContextDecorator>>();
        _loggerMock.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    private CorrelationContextDecorator CreateDecorator()
    {
        return new CorrelationContextDecorator(_innerMock.Object, _loggerMock.Object);
    }

    public sealed class MetadataEnrichment
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("correlation", "causation")]
        public async Task PreservesOriginalContextAndOverwritesOnlyCorrelationFields(string? correlationId, string? causationId)
        {
            var message = new TestMessage { CorrelationId = correlationId, CausationId = causationId };
            var handler = new object();
            var failureTime = DateTimeOffset.UtcNow;
            var metadata = ImmutableDictionary.Create<string, object>(StringComparer.OrdinalIgnoreCase)
                .Add("custom", handler).Add("CorrelationId", "old-correlation")
                .Add("CausationId", "old-causation").Add("MessageId", "old-message");
            var original = new ProcessingContext("component", metadata)
            {
                Handler = handler,
                HandlerType = handler.GetType(),
                RetryCount = 2,
                FirstFailureTime = failureTime
            };
            ProcessingContext captured = default;
            var inner = new CoreMessageProcessor((_, context, _) =>
            {
                captured = context;
                return ValueTask.CompletedTask;
            });
            var decorator = new CorrelationContextDecorator(inner, NullLogger<CorrelationContextDecorator>.Instance);

            var result = await decorator.ProcessAsync(message, original, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(correlationId ?? message.MessageId.ToString(), captured.Metadata["CorrelationId"]);
            Assert.Equal(causationId ?? string.Empty, captured.Metadata["CausationId"]);
            Assert.Equal(message.MessageId.ToString(), captured.Metadata["MessageId"]);
            Assert.Same(handler, captured.Metadata["CUSTOM"]);
            Assert.Same(metadata.KeyComparer, captured.Metadata.KeyComparer);
            Assert.Equal(original.Component, captured.Component);
            Assert.Same(handler, captured.Handler);
            Assert.Equal(original.HandlerType, captured.HandlerType);
            Assert.Equal(2, captured.RetryCount);
            Assert.Equal(failureTime, captured.FirstFailureTime);
            Assert.Same(metadata, original.Metadata);
            Assert.Equal("old-correlation", original.Metadata["CorrelationId"]);
            Assert.Equal("old-causation", original.Metadata["CausationId"]);
            Assert.Equal("old-message", original.Metadata["MessageId"]);
        }

        [Fact]
        public async Task SupportsDefaultInitializedContext()
        {
            var message = new TestMessage();
            ProcessingContext captured = default;
            var inner = new CoreMessageProcessor((_, context, _) =>
            {
                captured = context;
                return ValueTask.CompletedTask;
            });
            var decorator = new CorrelationContextDecorator(inner, NullLogger<CorrelationContextDecorator>.Instance);

            var result = await decorator.ProcessAsync(message, default, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(3, captured.Metadata.Count);
            Assert.Equal(message.MessageId.ToString(), captured.Metadata["CorrelationId"]);
            Assert.Equal(string.Empty, captured.Metadata["CausationId"]);
            Assert.Equal(message.MessageId.ToString(), captured.Metadata["MessageId"]);
        }

        [Fact]
        public async Task ConcurrentAsyncInvocationsKeepMetadataAndAmbientStateIsolated()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var metadata = ImmutableDictionary<string, object>.Empty.Add("custom", "unchanged");
            var original = new ProcessingContext("shared", metadata);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = 0;
            var inner = new CoreMessageProcessor(async (message, context, ct) =>
            {
                if (Interlocked.Increment(ref entered) == 32)
                    ready.TrySetResult();
                await release.Task.WaitAsync(ct);
                Assert.Equal(message.CorrelationId, context.Metadata["CorrelationId"]);
                Assert.Equal(message.CausationId, context.Metadata["CausationId"]);
                Assert.Equal(message.MessageId.ToString(), context.Metadata["MessageId"]);
                Assert.Equal(message.CorrelationId, CorrelationContext.CurrentCorrelationId);
                Assert.Equal(message.MessageId.ToString(), CorrelationContext.CurrentMessageId);
            });
            var decorator = new CorrelationContextDecorator(inner, NullLogger<CorrelationContextDecorator>.Instance);
            using var parent = CorrelationContext.BeginScope("parent-correlation", "parent-message");
            var tasks = Enumerable.Range(0, 32).Select(index => decorator.ProcessAsync(
                new TestMessage { CorrelationId = $"correlation-{index}", CausationId = $"causation-{index}" },
                original, cancellationToken).AsTask()).ToArray();

            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            finally
            {
                release.TrySetResult();
            }
            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            Assert.All(results, result => Assert.True(result.Success, result.Exception?.ToString()));
            Assert.Same(metadata, original.Metadata);
            Assert.Single(original.Metadata);
            Assert.Equal("parent-correlation", CorrelationContext.CurrentCorrelationId);
            Assert.Equal("parent-message", CorrelationContext.CurrentMessageId);
        }
    }

    public sealed class CorrelationContextSetup : CorrelationContextDecoratorTests
    {

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
                async () => await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken));

            // Assert - Context should be cleared even after exception
            Assert.Null(CorrelationContext.Current);
        }

    }

    public sealed class ContextEnrichment : CorrelationContextDecoratorTests
    {

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(capturedContext);
            var storedCausationId = capturedContext.Value.GetMetadataReference<string>("CausationId");
            Assert.Equal(string.Empty, storedCausationId);
        }

    }

    public sealed class InnerProcessorInvocation : CorrelationContextDecoratorTests
    {

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
            var result = await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            var result = await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            var result = await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(testException, result.Exception);
        }

    }

    public sealed class Logging : CorrelationContextDecoratorTests
    {

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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
            await decorator.ProcessAsync(message, context, cancellationToken: TestContext.Current.CancellationToken);

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

    }

    public sealed class Cancellation : CorrelationContextDecoratorTests
    {

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

    }

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

}
