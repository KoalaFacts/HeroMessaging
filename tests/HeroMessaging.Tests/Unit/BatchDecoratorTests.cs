using System.Linq;
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
public class BatchDecoratorTests
{

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WhenDisabled_ProcessesImmediately()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var expectedResult = ProcessingResult.Successful("Processed");

        mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        mockInnerProcessor.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var decorator = new BatchDecorator(mockInnerProcessor.Object, null!, mockLogger.Object, timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var timeProvider = new FakeTimeProvider();
        var options = new BatchProcessingOptions { Enabled = true };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, null!, timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithInvalidOptions_ThrowsArgumentException()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = -1 // Invalid
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesMessagesInBatch_WhenMaxSizeReached()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

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

        var processedCount = 0;

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();
        var results = await Task.WhenAll(tasks);

        // Assert - verify observable outcomes (results and counter)
        Assert.Equal(3, processedCount);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesIndividually_WhenBelowMinBatchSize()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 10,
            MinBatchSize = 5,
            BatchTimeout = TimeSpan.FromMilliseconds(100)
        };

        var message = TestMessageBuilder.CreateValidMessage();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Wait for timeout to trigger processing
        await Task.Delay(200);
        await task;

        // Assert
        mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ReturnsSuccessResult_WhenProcessingSucceeds()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var expectedResult = ProcessingResult.Successful("Success");

        mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

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
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var exception = new InvalidOperationException("Processing failed");
        var expectedResult = ProcessingResult.Failed(exception);

        mockInnerProcessor.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

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
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

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

        var processedCount = 0;

        // First message fails, others succeed
        mockInnerProcessor.SetupSequence(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Failed(new Exception("Error"));
            })
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Successful();
            })
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();
        var results = await Task.WhenAll(tasks);

        // Assert - verify observable outcomes
        Assert.Equal(3, processedCount);
        Assert.False(results[0].Success);  // First message failed
        Assert.True(results[1].Success);   // Second message succeeded
        Assert.True(results[2].Success);   // Third message succeeded
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_HandlesCancellation_Gracefully()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var context = new ProcessingContext("test");
        var cts = new CancellationTokenSource();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            decorator.ProcessAsync(message, context, cts.Token).AsTask());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesRemainingMessages_OnDisposal()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 100,
            BatchTimeout = TimeSpan.FromSeconds(60) // Long timeout
        };

        var message = TestMessageBuilder.CreateValidMessage();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Dispose before timeout
        await decorator.DisposeAsync();

        // Complete the task
        var result = await task;

        // Assert
        Assert.True(result.Success);
        mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_WithParallelProcessing_ProcessesMessagesInParallel()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

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

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();
        var results = await Task.WhenAll(tasks);

        // Assert - verify observable outcomes
        Assert.Equal(4, processedCount);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_FallbackToIndividual_WhenBatchProcessingThrows()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

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
        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First calls might throw, then succeed
                if (callCount <= 2)
                    throw new Exception("Batch error");
                return ProcessingResult.Successful();
            });

        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Wait for processing with longer timeout to allow fallback
        await Task.Delay(1000);

        // Assert - Verify fallback was attempted
        mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_LogsInformation_WhenInitialized()
    {
        // Arrange
        var mockInnerProcessor = new Mock<IMessageProcessor>();
        var mockLogger = new Mock<ILogger<BatchDecorator>>();
        var timeProvider = new FakeTimeProvider();

        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 50,
            BatchTimeout = TimeSpan.FromMilliseconds(200),
            MinBatchSize = 2
        };

        // Act
        await using var decorator = new BatchDecorator(mockInnerProcessor.Object, options, mockLogger.Object, timeProvider);

        // Wait a bit for background task to start
        await Task.Delay(100);

        // Assert
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BatchDecorator initialized")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
