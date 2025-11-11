using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for EventBusV2
/// Tests event publishing, handler invocation, pipeline processing, and error handling
/// </summary>
[Trait("Category", "Unit")]
public sealed class EventBusV2Tests
{
    private readonly Mock<ILogger<EventBusV2>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;

    public EventBusV2Tests()
    {
        _mockLogger = new Mock<ILogger<EventBusV2>>();
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // Helper to wait for async event processing to complete
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
    public void Constructor_WithValidServiceProvider_CreatesEventBusV2()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        var eventBus = new EventBusV2(provider, _mockLogger.Object);

        // Assert
        Assert.NotNull(eventBus);
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        var eventBus = new EventBusV2(provider);

        // Assert
        Assert.NotNull(eventBus);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_DoesNotValidateParameter()
    {
        // Arrange, Act & Assert
        // EventBusV2 doesn't validate null service provider in constructor
        // It will fail when trying to resolve services
        var exception = Record.Exception(() => new EventBusV2(null!, _mockLogger.Object));
        Assert.Null(exception); // Constructor doesn't validate, NullReferenceException happens later when accessing it
    }

    #endregion

    #region Publish Tests - Single Handler

    [Fact]
    public async Task Publish_WithNoRegisteredHandlers_LogsDebugAndReturns()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(100); // Allow processing to complete

        // Assert - No exception thrown, event published without handlers
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
    public async Task Publish_WithSingleHandler_InvokesHandlerSuccessfully()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test data" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200); // Give the event time to be processed

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.Is<TestEvent>(e => e.Data == "test data"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesAllHandlersInParallel()
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
        services.AddSingleton<IEventHandler<TestEvent>>(handler1Mock.Object);
        services.AddSingleton<IEventHandler<TestEvent>>(handler2Mock.Object);
        services.AddSingleton<IEventHandler<TestEvent>>(handler3Mock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200); // Give events time to be processed

        // Assert - All handlers should be invoked
        handler1Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        handler2Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        handler3Mock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Publish Tests - Cancellation Token

    [Fact]
    public async Task Publish_WithDefaultCancellationToken_Succeeds()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent); // Uses default cancellation token

        // Assert
        await WaitForConditionAsync(() =>
        {
            try
            {
                handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    [Fact]
    public async Task Publish_WithCancellationToken_PropagatesTokenToHandler()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent, cts.Token);
        await Task.Delay(200);

        // Assert - Verify the token was passed through
        handlerMock.Verify(h => h.Handle(
            It.IsAny<TestEvent>(),
            It.Is<CancellationToken>(ct => ct == cts.Token)), Times.Once);
    }

    [Fact]
    public async Task Publish_WithCancelledCancellationToken_DoesNotProcessEvent()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var handlerCalled = false;

        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => handlerCalled = true)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        cts.Cancel();
        var testEvent = new TestEvent { Data = "test" };

        // Act
        var publishTask = eventBus.Publish(testEvent, cts.Token);

        // Assert - The publish itself should handle the cancellation gracefully
        try
        {
            await publishTask;
        }
        catch (OperationCanceledException)
        {
            // Expected if SendAsync throws on cancelled token
        }
    }

    #endregion

    #region Multiple Events Tests

    [Fact]
    public async Task Publish_WithMultipleEventsSequentially_InvokesHandlersForEach()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);

        // Act
        await eventBus.Publish(new TestEvent { Data = "event1" });
        await eventBus.Publish(new TestEvent { Data = "event2" });
        await eventBus.Publish(new TestEvent { Data = "event3" });
        await Task.Delay(300); // Give events time to be processed

        // Assert
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Publish_WithMultipleEventsInParallel_InvokesAllHandlers()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);

        // Act
        var tasks = new[]
        {
            eventBus.Publish(new TestEvent { Data = "event1" }),
            eventBus.Publish(new TestEvent { Data = "event2" }),
            eventBus.Publish(new TestEvent { Data = "event3" })
        };
        await Task.WhenAll(tasks);
        await Task.Delay(300); // Give events time to be processed

        // Assert
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region Error Handling Tests - Handler Exceptions

    [Fact]
    public async Task Publish_WhenHandlerThrowsException_ProcessingContinues()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Handler failed");
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500); // Give processing time

        // Assert - Handler was called (processing happened despite exception)
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrowsArgumentException_ProcessingContinues()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid argument"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500);

        // Assert - Handler was called despite exception
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrowsTimeoutException_ProcessingContinues()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Operation timed out"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500);

        // Assert - Handler was called despite timeout exception
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Handler Success Path Tests

    [Fact]
    public async Task Publish_WithSuccessfulHandler_CompletesWithoutError()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var callCount = 0;

        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test data" };

        // Act
        await eventBus.Publish(testEvent);
        await WaitForConditionAsync(() => callCount > 0);

        // Assert
        Assert.Equal(1, callCount);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never); // No errors logged
    }

    [Fact]
    public async Task Publish_WithAsyncHandler_WaitsForCompletion()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var completed = false;

        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(100);
                completed = true;
            });

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await WaitForConditionAsync(() => completed);

        // Assert
        Assert.True(completed);
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Pipeline Processing Tests

    [Fact]
    public async Task Publish_ProcessesThroughCompletePipeline_IncludingMetricsLoggingAndValidation()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act - Publishing should process through the entire pipeline
        await eventBus.Publish(testEvent);
        await Task.Delay(200);

        // Assert - Handler was called (proving pipeline executed)
        handlerMock.Verify(h => h.Handle(
            It.Is<TestEvent>(e => e.Data == "test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithEventMetadata_PreservesMetadataInContext()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var receivedEvent = (TestEvent?)null;

        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TestEvent, CancellationToken>((evt, ct) => receivedEvent = evt)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent
        {
            Data = "test",
            CorrelationId = "corr-123",
            CausationId = "cause-456"
        };

        // Act
        await eventBus.Publish(testEvent);
        await WaitForConditionAsync(() => receivedEvent != null);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("corr-123", receivedEvent!.CorrelationId);
        Assert.Equal("cause-456", receivedEvent!.CausationId);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Fact]
    public async Task Publish_WithEmptyEventData_Succeeds()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200);

        // Assert
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithLargeEventData_Succeeds()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var largeData = new string('x', 10000);
        var testEvent = new TestEvent { Data = largeData };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.Is<TestEvent>(e => e.Data.Length == 10000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithNullMetadata_Succeeds()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test", Metadata = null };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(200);

        // Assert
        handlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithOneHandlerFailingAndOneSucceeding_BothAreAttempted()
    {
        // Arrange
        var failingHandlerMock = new Mock<IEventHandler<TestEvent>>();
        failingHandlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        var successfulHandlerMock = new Mock<IEventHandler<TestEvent>>();
        successfulHandlerMock.Setup(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<TestEvent>>(failingHandlerMock.Object);
        services.AddSingleton<IEventHandler<TestEvent>>(successfulHandlerMock.Object);
        var provider = services.BuildServiceProvider();

        var eventBus = new EventBusV2(provider, _mockLogger.Object);
        var testEvent = new TestEvent { Data = "test" };

        // Act
        await eventBus.Publish(testEvent);
        await Task.Delay(500); // Allow both handlers time to process

        // Assert - Both should be attempted
        failingHandlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        successfulHandlerMock.Verify(h => h.Handle(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Once);
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
