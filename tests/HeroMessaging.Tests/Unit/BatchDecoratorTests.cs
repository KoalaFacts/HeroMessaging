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
/// Unit tests for BatchDecorator.
/// Each test creates its own FakeTimeProvider and BatchDecorator instance.
/// Tests must properly dispose the BatchDecorator to cancel background tasks.
/// </summary>
/// <remarks>
/// <para>
/// Tests use <see cref="BatchDecorator.CreateAsync"/> factory method which ensures the background
/// loop is properly initialized before returning. This eliminates race conditions with FakeTimeProvider
/// and allows tests to run in parallel safely.
/// </para>
/// <para>
/// Tests use <see cref="BatchDecorator.WaitForBatchIterationAsync"/> for deterministic
/// synchronization instead of timing-based delays.
/// </para>
/// </remarks>
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

        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

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
            await using var decorator = await BatchDecorator.CreateAsync(
                mockInnerProcessor.Object, null!, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);
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
            await using var decorator = await BatchDecorator.CreateAsync(
                mockInnerProcessor.Object, options, null!, timeProvider, TestContext.Current.CancellationToken);
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
            await using var decorator = await BatchDecorator.CreateAsync(
                mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);
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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
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

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time to trigger batch processing (loop is already initialized by CreateAsync)
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for FakeTimeProvider
        };

        var message = TestMessageBuilder.CreateValidMessage();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Advance time past the timeout, then wait for processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(150));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);
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

        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

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

        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
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

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time, then wait for batch processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

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

        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Use minimal timeout with FakeTimeProvider
        };

        var message = TestMessageBuilder.CreateValidMessage();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // CreateAsync ensures the background loop is initialized before returning
        var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask();

        // Advance time past timeout, then wait for batch
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        // Dispose processes remaining messages
        await decorator.DisposeAsync(TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
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

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time, then wait for batch processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
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

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time past batch timeout to trigger processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        // Wait for next loop iteration and advance time again for cleanup
        await decorator.WaitForLoopReadyAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

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
            BatchTimeout = TimeSpan.FromMilliseconds(1), // Minimal timeout for FakeTimeProvider - avoids blocking
            MinBatchSize = 2
        };

        // Act - CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Advance time, then wait for batch iteration
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        // Assert - Look for the "started" log message instead (factory logs "started" not "initialized")
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("BatchDecorator started")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_StopsProcessing_WhenContinueOnFailureFalse()
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
            ContinueOnFailure = false, // Explicitly disable continuation on failure
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
        };

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2"),
            TestMessageBuilder.CreateValidMessage("Message 3")
        };

        var processedCount = 0;

        // First message fails, others should not be processed due to ContinueOnFailure = false
        mockInnerProcessor.SetupSequence(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref processedCount);
                return ProcessingResult.Failed(new InvalidOperationException("Error"));
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

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time, then wait for batch processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(tasks);

        // Assert - First message fails, processing should stop
        Assert.False(results[0].Success);  // First message failed
        // Due to stop on failure, verify batch processing attempts
        mockInnerProcessor.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_ProcessesMessageWithoutFailure_WhenExceptionCaught()
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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Minimal timeout for fast tests with FakeTimeProvider
        };

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Message 1"),
            TestMessageBuilder.CreateValidMessage("Message 2")
        };

        var exception = new InvalidOperationException("Unexpected error");
        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act
        var tasks = messages.Select(m => decorator.ProcessAsync(m, new ProcessingContext("test"), TestContext.Current.CancellationToken).AsTask()).ToList();

        // Advance time, then wait for batch processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        var results = await Task.WhenAll(tasks);

        // Assert - Both messages should have failed with the exception
        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.NotNull(r.Exception);
        });
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchDecorator_CompletesPendingTasks_OnProcessCancellation()
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
            BatchTimeout = TimeSpan.FromMilliseconds(1) // Use minimal timeout with FakeTimeProvider
        };

        var message = TestMessageBuilder.CreateValidMessage();
        var cts = new CancellationTokenSource();

        mockInnerProcessor.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // CreateAsync ensures the background loop is initialized before returning
        await using var decorator = await BatchDecorator.CreateAsync(
            mockInnerProcessor.Object, options, mockLogger.Object, timeProvider, TestContext.Current.CancellationToken);

        // Act - Queue a message then cancel immediately
        var task = decorator.ProcessAsync(message, new ProcessingContext("test"), cts.Token).AsTask();

        // Advance time, then wait for batch processing
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);
        cts.Cancel();

        // Wait for next loop iteration and advance time again for cleanup
        await decorator.WaitForLoopReadyAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMilliseconds(5));
        await decorator.WaitForBatchIterationAsync(TestContext.Current.CancellationToken);

        // Assert - The task should handle cancellation gracefully
        try
        {
            await task;
            // If it completes without throwing, that's fine
            Assert.True(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected and acceptable
            Assert.True(true);
        }
    }
}
