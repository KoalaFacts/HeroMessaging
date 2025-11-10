using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for BatchDecorator
/// </summary>
public class BatchDecoratorTests : IAsyncLifetime
{
    private readonly Mock<IMessageProcessor> _mockInnerProcessor;
    private readonly Mock<ILogger<BatchDecorator>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;

    public BatchDecoratorTests()
    {
        _mockInnerProcessor = new Mock<IMessageProcessor>();
        _mockLogger = new Mock<ILogger<BatchDecorator>>();
        _timeProvider = new FakeTimeProvider();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WhenDisabled_ProcessesImmediately()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var expectedResult = ProcessingResult.Successful("Processed");

        _mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        _mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, null!, _mockLogger.Object, _timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new BatchProcessingOptions { Enabled = true };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, null!, _timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithInvalidOptions_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = -1 // Invalid
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesMessagesInBatch_WhenMaxSizeReached()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 3,
            MinBatchSize = 2,
            BatchTimeout = TimeSpan.FromSeconds(10)
        };

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2"),
            TestMessageBuilder.CreateValidMessage("Message 3")
        };

        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Wait for all to complete
        await Task.WhenAll(tasks);

        // Give background task time to process
        await Task.Delay(500);

        // Assert
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesIndividually_WhenBelowMinBatchSize()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 10,
            MinBatchSize = 5,
            BatchTimeout = TimeSpan.FromMilliseconds(100)
        };

        var message = TestMessageBuilder.CreateValidMessage();

        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Wait for timeout to trigger processing
        await Task.Delay(200);
        await task;

        // Assert
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ReturnsSuccessResult_WhenProcessingSucceeds()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var expectedResult = ProcessingResult.Successful("Success");

        _mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Success", result.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ReturnsFailureResult_WhenProcessingFails()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var exception = new InvalidOperationException("Processing failed");
        var expectedResult = ProcessingResult.Failed(exception);

        _mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ContinuesProcessing_WhenContinueOnFailureEnabled()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 3,
            MinBatchSize = 2,
            ContinueOnFailure = true,
            BatchTimeout = TimeSpan.FromSeconds(10)
        };

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2"),
            TestMessageBuilder.CreateValidMessage("Message 3")
        };

        // First message fails, others succeed
        _mockInnerProcessor.SetupSequence(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(new Exception("Error")))
            .ReturnsAsync(ProcessingResult.Successful())
            .ReturnsAsync(ProcessingResult.Successful());

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();
        await Task.WhenAll(tasks);

        // Give background task time to process
        await Task.Delay(500);

        // Assert
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_HandlesCancellation_Gracefully()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var cts = new CancellationTokenSource();

        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            decorator.ProcessAsync(message, context, cts.Token).AsTask());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesRemainingMessages_OnDisposal()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 100,
            BatchTimeout = TimeSpan.FromSeconds(60) // Long timeout
        };

        var message = TestMessageBuilder.CreateValidMessage();

        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Dispose before timeout
        await decorator.DisposeAsync();

        // Complete the task
        var result = await task;

        // Assert
        Assert.True(result.Success);
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithParallelProcessing_ProcessesMessagesInParallel()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 4,
            MinBatchSize = 2,
            MaxDegreeOfParallelism = 4,
            BatchTimeout = TimeSpan.FromSeconds(10)
        };

        var messages = Enumerable.Range(0, 4)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Message {i}"))
            .ToArray();

        var processedCount = 0;
        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();
        await Task.WhenAll(tasks);

        // Give background task time to process
        await Task.Delay(500);

        // Assert
        Assert.Equal(4, processedCount);
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_FallbackToIndividual_WhenBatchProcessingThrows()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 2,
            MinBatchSize = 2,
            FallbackToIndividualProcessing = true,
            BatchTimeout = TimeSpan.FromSeconds(10)
        };

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2")
        };

        // Setup to throw on first call, succeed on subsequent calls
        var callCount = 0;
        _mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First calls might throw, then succeed
                if (callCount <= 2)
                    throw new Exception("Batch error");
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Wait for processing with longer timeout to allow fallback
        await Task.Delay(1000);

        // Assert - Verify fallback was attempted
        _mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_LogsInformation_WhenInitialized()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 50,
            BatchTimeout = TimeSpan.FromMilliseconds(200),
            MinBatchSize = 2
        };

        // Act
        await using var decorator = new BatchDecorator(_mockInnerProcessor.Object, options, _mockLogger.Object, _timeProvider);

        // Wait a bit for background task to start
        await Task.Delay(100);

        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BatchDecorator initialized")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
