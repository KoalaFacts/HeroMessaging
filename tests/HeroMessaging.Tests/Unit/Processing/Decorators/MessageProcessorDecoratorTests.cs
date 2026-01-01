using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing.Decorators;

[Trait("Category", "Unit")]
public class MessageProcessorDecoratorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidInner_CreatesInstance()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();

        // Act
        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TestMessageProcessorDecorator(null!));

        Assert.Equal("inner", exception.ParamName);
    }

    #endregion

    #region ProcessAsync Tests

    [Fact]
    public async Task ProcessAsync_CallsInnerProcessor()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedResult = new ProcessingResult { Success = true };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        innerMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(expectedResult.Success, result.Success);
    }

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PassesTokenToInner()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var expectedResult = new ProcessingResult { Success = true };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, cts.Token, TestContext.Current.CancellationToken);

        // Assert
        innerMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
        Assert.Equal(expectedResult.Success, result.Success);
    }

    [Fact]
    public async Task ProcessAsync_WhenInnerSucceeds_ReturnsSuccessResult()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedResult = new ProcessingResult { Success = true };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_WhenInnerFails_ReturnsFailureResult()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedResult = new ProcessingResult { Success = false, Message = "Processing failed" };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Processing failed", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WhenInnerThrows_PropagatesException()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedException = new InvalidOperationException("Inner processor error");

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken));

        Assert.Equal("Inner processor error", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithCancelledToken_PropagatesCancellation()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        innerMock
            .Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await decorator.ProcessAsync(message, context, cts.Token, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Decorator Chaining Tests

    [Fact]
    public async Task ProcessAsync_WithMultipleDecorators_CallsInCorrectOrder()
    {
        // Arrange
        var callOrder = new List<string>();
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callOrder.Add("Inner");
                return new ProcessingResult { Success = true };
            });

        var decorator1 = new TrackingDecorator(innerMock.Object, callOrder, "Decorator1");
        var decorator2 = new TrackingDecorator(decorator1, callOrder, "Decorator2");

        // Act
        await decorator2.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, callOrder.Count);
        Assert.Equal("Decorator2", callOrder[0]);
        Assert.Equal("Decorator1", callOrder[1]);
        Assert.Equal("Inner", callOrder[2]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task ProcessAsync_WithNullMetadata_ProcessesSuccessfully()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage { Metadata = null };
        var context = new ProcessingContext();
        var expectedResult = new ProcessingResult { Success = true };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessAsync_WithEmptyContext_ProcessesSuccessfully()
    {
        // Arrange
        var innerMock = new Mock<IMessageProcessor>();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedResult = new ProcessingResult { Success = true };

        innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new TestMessageProcessorDecorator(innerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
    }

    #endregion

    #region Helper Classes

    public class TestMessageProcessorDecorator : MessageProcessorDecorator
    {
        public TestMessageProcessorDecorator(IMessageProcessor inner) : base(inner)
        {
        }

        public override async ValueTask<ProcessingResult> ProcessAsync(
            IMessage message,
            ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            return await base.ProcessAsync(message, context, cancellationToken);
        }
    }

    public class TrackingDecorator : MessageProcessorDecorator
    {
        private readonly List<string> _callOrder;
        private readonly string _name;

        public TrackingDecorator(IMessageProcessor inner, List<string> callOrder, string name) : base(inner)
        {
            _callOrder = callOrder;
            _name = name;
        }

        public override async ValueTask<ProcessingResult> ProcessAsync(
            IMessage message,
            ProcessingContext context,
            CancellationToken cancellationToken = default)
        {
            _callOrder.Add(_name);
            return await base.ProcessAsync(message, context, cancellationToken);
        }
    }

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    #endregion
}
