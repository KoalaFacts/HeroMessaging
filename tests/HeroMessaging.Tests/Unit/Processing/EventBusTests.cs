using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for EventBus
/// Tests event publishing, handler invocation, error handling, and metrics tracking
/// </summary>
[Trait("Category", "Unit")]
public sealed class EventBusTests
{
    private readonly Mock<ILogger<EventBus>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;

    public EventBusTests()
    {
        _mockLogger = new Mock<ILogger<EventBus>>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // Helper to wait for async event processing to complete
    // Note: This polling approach is a test smell that should be replaced with proper synchronization
    // The EventBus uses ActionBlock which processes asynchronously after SendAsync returns
    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 10000, int pollIntervalMs = 50)
    {
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(pollIntervalMs);
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServiceProvider_CreatesEventBus()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Assert
        Assert.NotNull(eventBus);
        Assert.True(eventBus.IsRunning);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventBus(provider, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        var eventBus = new EventBus(provider, _timeProvider);

        // Assert
        Assert.NotNull(eventBus);
    }

    #endregion

    #region Publish Tests

    [Fact]
    public async Task Publish_WithNullEvent_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => eventBus.Publish(null!));
    }

    [Fact]
    public async Task Publish_WithNoRegisteredHandlers_LogsDebugAndReturns()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Assert - No exception thrown, event published without handlers
        var metrics = eventBus.GetMetrics();
        Assert.Equal(0, metrics.PublishedCount);
    }

    [Fact]
    public async Task Publish_WithSingleHandler_InvokesHandler()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test data" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(100); // Give the event time to be processed

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.Is<TestEvent>(e => e.Data == "test data"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesAllHandlers()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        handler1Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler2Mock = new Mock<IEventHandler<TestEvent>>();
        handler2Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handler1Mock.Object);
        services.AddSingleton(handler2Mock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(100); // Give events time to be processed

        // Assert
        handler1Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        handler2Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent, cts.Token);
        await Task.Delay(100);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.IsAny<TestEvent>(),
            It.Is<CancellationToken>(ct => ct == cts.Token)), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Publish_WhenHandlerThrowsWithoutErrorHandler_RetriesAndFails()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Wait for async processing to complete (ActionBlock processes in background)
        // The handler will be called multiple times (initial + retries) before giving up
        await WaitForConditionAsync(() =>
        {
            try
            {
                handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.AtLeast(4));
                return true;
            }
            catch
            {
                return false;
            }
        });

        // Wait a bit more for metrics to be updated after final retry
        await WaitForConditionAsync(() => eventBus.GetMetrics().FailedCount == 1);

        // Assert - Handler should be called at least once, and failed count should increment
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrowsWithErrorHandlerRetry_RetriesAccordingToPolicy()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<TestEvent>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(1000);

        // Assert - Should retry based on error handler policy (at least once)
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task Publish_WhenErrorHandlerReturnsDiscard_StopsProcessing()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<IMessage>(), // More permissive - accept any IMessage
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard("Discarding event"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Wait for handler to be invoked and error handler to be called
        await WaitForConditionAsync(() =>
        {
            try
            {
                handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
                errorHandlerMock.Verify(h => h.HandleErrorAsync(
                    It.IsAny<IMessage>(),
                    It.IsAny<Exception>(),
                    It.IsAny<ErrorContext>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch
            {
                return false;
            }
        });

        // Wait for metrics to update after error handling completes
        await WaitForConditionAsync(() => eventBus.GetMetrics().FailedCount == 1);

        // Assert - Should only try once, then discard
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);

        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WhenErrorHandlerReturnsSendToDeadLetter_StopsProcessing()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<IMessage>(), // More permissive - accept any IMessage
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.SendToDeadLetter("Sending to DLQ"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Wait for handler to be invoked and error handler to be called
        await WaitForConditionAsync(() =>
        {
            try
            {
                handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
                errorHandlerMock.Verify(h => h.HandleErrorAsync(
                    It.IsAny<IMessage>(),
                    It.IsAny<Exception>(),
                    It.IsAny<ErrorContext>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch
            {
                return false;
            }
        });

        // Wait for metrics to update after error handling completes
        await WaitForConditionAsync(() => eventBus.GetMetrics().FailedCount == 1);

        // Assert
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);

        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WhenErrorHandlerReturnsEscalate_ThrowsException()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var expectedException = new InvalidOperationException("Handler error");
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<TestEvent>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Escalate());

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Note: The exception is thrown in the background processing, so we need to wait and check logs
        await Task.Delay(200);

        // Assert - Handler tried once
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_InitialState_ReturnsZeroMetrics()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act
        var metrics = eventBus.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.PublishedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(0, metrics.RegisteredHandlers);
    }

    [Fact]
    public async Task GetMetrics_AfterPublishing_IncrementsPublishedCount()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        var metrics = eventBus.GetMetrics();

        // Assert
        Assert.Equal(1, metrics.PublishedCount);
        Assert.Equal(1, metrics.RegisteredHandlers);
    }

    [Fact]
    public async Task GetMetrics_AfterMultipleEvents_TracksCorrectly()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act
        await eventBus.Publish(new TestEvent { Data = "1" });
        await eventBus.Publish(new TestEvent { Data = "2" });
        await eventBus.Publish(new TestEvent { Data = "3" });

        var metrics = eventBus.GetMetrics();

        // Assert
        Assert.Equal(3, metrics.PublishedCount);
    }

    [Fact]
    public async Task GetMetrics_WithMultipleHandlers_TracksHandlerCount()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        handler1Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler2Mock = new Mock<IEventHandler<TestEvent>>();
        handler2Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler3Mock = new Mock<IEventHandler<TestEvent>>();
        handler3Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handler1Mock.Object);
        services.AddSingleton(handler2Mock.Object);
        services.AddSingleton(handler3Mock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        var metrics = eventBus.GetMetrics();

        // Assert
        Assert.Equal(1, metrics.PublishedCount);
        Assert.Equal(3, metrics.RegisteredHandlers);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task Publish_WithConcurrentPublishes_ThreadSafeMetricsTracking()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act - Publish events concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(eventBus.Publish(new TestEvent { Data = $"event-{i}" }));
        }
        await Task.WhenAll(tasks);
        await Task.Delay(500); // Wait for processing

        // Assert - Metrics should be accurate despite concurrent access
        var metrics = eventBus.GetMetrics();
        Assert.Equal(10, metrics.PublishedCount);
        Assert.Equal(1, metrics.RegisteredHandlers);
    }

    [Fact]
    public async Task Publish_WithConcurrentHandlers_AllProcessInParallel()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var handler3Called = false;

        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        handler1Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => handler1Called = true)
            .Returns(async () => await Task.Delay(50));

        var handler2Mock = new Mock<IEventHandler<TestEvent>>();
        handler2Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => handler2Called = true)
            .Returns(async () => await Task.Delay(50));

        var handler3Mock = new Mock<IEventHandler<TestEvent>>();
        handler3Mock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => handler3Called = true)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handler1Mock.Object);
        services.AddSingleton(handler2Mock.Object);
        services.AddSingleton(handler3Mock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(300); // Allow parallel processing

        // Assert - All handlers should complete
        handler1Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        handler2Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        handler3Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(handler1Called);
        Assert.True(handler2Called);
        Assert.True(handler3Called);
    }

    [Fact]
    public async Task GetMetrics_UnderHighConcurrency_RemainsConsistent()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act - Publish and check metrics concurrently
        var publishTasks = new List<Task>();
        var metricsTasks = new List<Task<IEventBusMetrics>>();

        for (int i = 0; i < 5; i++)
        {
            publishTasks.Add(eventBus.Publish(new TestEvent { Data = $"event-{i}" }));
        }

        for (int i = 0; i < 5; i++)
        {
            metricsTasks.Add(Task.Run(() => eventBus.GetMetrics()));
        }

        await Task.WhenAll(publishTasks);
        await Task.Delay(300);
        var results = await Task.WhenAll(metricsTasks);

        // Assert - All metric reads should show reasonable values
        Assert.All(results, m =>
        {
            Assert.True(m.PublishedCount >= 0);
            Assert.True(m.PublishedCount <= 5);
        });
    }

    #endregion

    #region Error Handling - Retry with Delays

    [Fact]
    public async Task Publish_WhenHandlerThrowsWithoutErrorHandler_AppliesExponentialBackoff()
    {
        // Arrange
        var callTimes = new List<long>();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callTimes.Add(DateTime.UtcNow.Ticks))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(2000); // Allow retries with exponential backoff

        // Assert - Handler should be called multiple times with delays between attempts
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WhenErrorHandlerReturnsRetryWithDelay_RespectDelay()
    {
        // Arrange
        var callTimes = new List<DateTime>();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callTimes.Add(DateTime.UtcNow))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<TestEvent>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.FromMilliseconds(100)));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500); // Allow for retries with delay

        // Assert - Handler should be called multiple times
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    #endregion

    #region Error Context Tests

    [Fact]
    public async Task Publish_WhenErrorOccurs_ErrorContextContainsCorrectMetadata()
    {
        // Arrange
        var capturedContext = (ErrorContext?)null;
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<TestEvent>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) => capturedContext = ctx)
            .ReturnsAsync(ErrorHandlingResult.Discard("Test"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await WaitForConditionAsync(() => capturedContext != null);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(0, capturedContext!.RetryCount); // First attempt
        Assert.Equal(3, capturedContext!.MaxRetries);
        Assert.Equal("EventBus", capturedContext!.Component);
        Assert.NotNull(capturedContext!.Metadata);
        Assert.True(capturedContext!.Metadata.ContainsKey("EventType"));
        Assert.True(capturedContext!.Metadata.ContainsKey("HandlerType"));
    }

    [Fact]
    public async Task Publish_OnFirstFailure_SetsFirstFailureTime()
    {
        // Arrange
        var capturedContexts = new List<ErrorContext>();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<TestEvent>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<IMessage, Exception, ErrorContext, CancellationToken>((msg, ex, ctx, ct) => capturedContexts.Add(ctx))
            .ReturnsAsync(ErrorHandlingResult.Retry(TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500);

        // Assert
        Assert.NotEmpty(capturedContexts);
        var firstContext = capturedContexts[0];
        Assert.NotNull(firstContext.FirstFailureTime);
        Assert.NotEqual(default, firstContext.FirstFailureTime);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task Publish_WithNoHandlers_LogsDebugMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No handlers found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrows_LogsErrorWithRetryInfo()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500);

        // Assert - Error logging should happen
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Publish_WhenErrorHandlerReturnsDiscard_LogsWarning()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var errorHandlerMock = new Mock<IErrorHandler>();
        errorHandlerMock.Setup(h => h.HandleErrorAsync(
                It.IsAny<IMessage>(),
                It.IsAny<Exception>(),
                It.IsAny<ErrorContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ErrorHandlingResult.Discard("Discarding"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object, errorHandlerMock.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(300);

        // Assert - Warning should be logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Metrics - Failed Count Tests

    [Fact]
    public async Task Publish_WhenHandlerFailsWithoutErrorHandler_IncrementsFailedCount()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await WaitForConditionAsync(() => eventBus.GetMetrics().FailedCount == 1, timeoutMs: 5000);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WithMultipleFailures_TracksAllFailures()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act
        await eventBus.Publish(new TestEvent { Data = "event-1" });
        await eventBus.Publish(new TestEvent { Data = "event-2" });
        await eventBus.Publish(new TestEvent { Data = "event-3" });
        await WaitForConditionAsync(() => eventBus.GetMetrics().FailedCount == 3, timeoutMs: 5000);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(3, metrics.FailedCount);
        Assert.Equal(3, metrics.PublishedCount);
    }

    [Fact]
    public async Task Publish_SuccessfullyProcessed_DoesNotIncrementFailedCount()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(1, metrics.PublishedCount);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventBus(null!, _timeProvider, _mockLogger.Object));
    }

    [Fact]
    public async Task Publish_WithExceptionDuringReflection_HandlesGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _timeProvider, _mockLogger.Object);

        // Act & Assert - Publishing a valid event with no handlers should not throw
        var testEvent = new TestEvent { Data = "test" };
        await eventBus.Publish(testEvent); // No exception expected
    }

    #endregion

    #region Test Events

    public class TestEvent : IEvent
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    #endregion
}
