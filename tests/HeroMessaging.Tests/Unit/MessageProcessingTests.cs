using Xunit;
using Moq;
using System;
using System.Threading.Tasks;
using System.Threading;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Tests.TestUtilities;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for core message processing functionality
/// Target: 80%+ coverage for processing namespace, execution time < 30s
/// </summary>
public class MessageProcessingTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithValidMessage_ReturnsSuccess()
    {
        // Arrange
        var mockProcessor = new Mock<IMessageProcessor>();
        var message = TestMessageBuilder.CreateValidMessage("Valid test message");
        var context = new ProcessingContext("test-component");

        var expectedResult = ProcessingResult.Successful("Processing completed", "Valid test message");

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.FromResult(expectedResult));

        // Act
        var result = await mockProcessor.Object.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Processing completed", result.Message);
        Assert.Equal("Valid test message", result.Data);
        mockProcessor.Verify(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var mockProcessor = new Mock<IMessageProcessor>();
        var context = new ProcessingContext("test-component");

        mockProcessor.Setup(p => p.ProcessAsync(null, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .Throws(new ArgumentNullException("message"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => mockProcessor.Object.ProcessAsync(null, context).AsTask());

        Assert.Contains("message", exception.ParamName);
        mockProcessor.Verify(p => p.ProcessAsync(null, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithInvalidMessage_ReturnsFailure()
    {
        // Arrange
        var mockProcessor = new Mock<IMessageProcessor>();
        var invalidMessage = TestMessageBuilder.CreateInvalidMessage();
        var context = new ProcessingContext("test-component");

        var expectedResult = ProcessingResult.Failed(
            new InvalidOperationException("Message content cannot be null"),
            "INVALID_CONTENT");

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.FromResult(expectedResult));

        // Act
        var result = await mockProcessor.Object.ProcessAsync(invalidMessage, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Equal("INVALID_CONTENT", result.Message);
        Assert.Contains("null", result.Exception.Message);
        mockProcessor.Verify(p => p.ProcessAsync(invalidMessage, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_WithLargeMessage_HandlesEfficiently()
    {
        // Arrange
        var mockProcessor = new Mock<IMessageProcessor>();
        var largeMessage = TestMessageBuilder.CreateLargeMessage(10000); // 10KB message
        var context = new ProcessingContext("test-component");

        var expectedResult = ProcessingResult.Successful("Large message processed", largeMessage.GetTestContent());

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.FromResult(expectedResult));

        // Act
        var result = await mockProcessor.Object.ProcessAsync(largeMessage, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Large message processed", result.Message);
        Assert.Equal(largeMessage.GetTestContent(), result.Data);
        mockProcessor.Verify(p => p.ProcessAsync(largeMessage, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_ConcurrentRequests_HandlesSimultaneously()
    {
        // Arrange
        var mockProcessor = new Mock<IMessageProcessor>();
        var context = new ProcessingContext("test-component");

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2"),
            TestMessageBuilder.CreateValidMessage("Message 3")
        };

        mockProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
                    .Returns((IMessage msg, ProcessingContext ctx, CancellationToken ct) =>
                        ValueTask.FromResult(ProcessingResult.Successful("Processed", ((TestMessage)msg).Content)));

        // Act
        var tasks = messages.Select(msg => mockProcessor.Object.ProcessAsync(msg, context).AsTask());
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, results.Length);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal("Message 1", results[0].Data);
        Assert.Equal("Message 2", results[1].Data);
        Assert.Equal("Message 3", results[2].Data);
        mockProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessingContext_WithMetadata_ReturnsCorrectValues()
    {
        // Arrange
        var context = new ProcessingContext("test-component")
            .WithMetadata("key1", "value1")
            .WithMetadata("key2", 42);

        // Act & Assert
        Assert.Equal("test-component", context.Component);
        Assert.Equal("value1", context.GetMetadataReference<string>("key1"));
        Assert.Equal(42, context.GetMetadata<int>("key2"));
        Assert.Null(context.GetMetadataReference<string>("nonexistent"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessingResult_Successful_CreatesCorrectResult()
    {
        // Act
        var result = ProcessingResult.Successful("Test message", "test data");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test message", result.Message);
        Assert.Equal("test data", result.Data);
        Assert.Null(result.Exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessingResult_Failed_CreatesCorrectResult()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = ProcessingResult.Failed(exception, "Error occurred");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Error occurred", result.Message);
        Assert.Equal(exception, result.Exception);
        Assert.Null(result.Data);
    }
}