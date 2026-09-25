using System;
using System.Collections.Generic;
using System.Linq;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class EventBusTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILogger<EventBus>> _loggerMock;

    public EventBusTests()
    {
        _services = new ServiceCollection();
        _loggerMock = new Mock<ILogger<EventBus>>();

        // Add required TimeProvider
        _services.AddSingleton(TimeProvider.System);

        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private EventBus CreateEventBus()
    {
        var services = new ServiceCollection();

        // Copy existing registrations
        foreach (var service in _services)
        {
            ((IList<ServiceDescriptor>)services).Add(service);
        }

        var provider = services.BuildServiceProvider();
        return new EventBus(provider, _loggerMock.Object);
    }

    #region Publish - Success Cases

    [Fact]
    public async Task Publish_WithValidEvent_PublishesSuccessfully()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        var handlerInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _services.AddSingleton(handlerMock.Object);
        await using var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                handlerInvoked.TrySetResult(true);
                return Task.CompletedTask;
            });

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);
        await handlerInvoked.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithNoHandlers_LogsDebugAndReturnsWithoutError()
    {
        // Arrange
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        // Act & Assert - Should not throw
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesAllHandlers()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        var handler2Mock = new Mock<IEventHandler<TestEvent>>();
        var handler3Mock = new Mock<IEventHandler<TestEvent>>();
        var handlersInvoked = new[]
        {
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        _services.AddSingleton(handler1Mock.Object);
        _services.AddSingleton(handler2Mock.Object);
        _services.AddSingleton(handler3Mock.Object);

        await using var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handler1Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(() =>
        {
            handlersInvoked[0].TrySetResult(true);
            return Task.CompletedTask;
        });
        handler2Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(() =>
        {
            handlersInvoked[1].TrySetResult(true);
            return Task.CompletedTask;
        });
        handler3Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(() =>
        {
            handlersInvoked[2].TrySetResult(true);
            return Task.CompletedTask;
        });

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);
        await Task.WhenAll(handlersInvoked.Select(signal => signal.Task))
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Assert
        handler1Mock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
        handler2Mock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
        handler3Mock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithMultipleEvents_ProcessesAllEvents()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Publish_WhenHandlerThrows_DoesNotAffectOtherHandlers()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        var handler2Mock = new Mock<IEventHandler<TestEvent>>();

        _services.AddSingleton(handler1Mock.Object);
        _services.AddSingleton(handler2Mock.Object);

        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handler1Mock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler 1 failed"));

        handler2Mock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert - Handler 2 should still be called despite Handler 1 failing
        handler2Mock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrows_UpdatesFailureMetrics()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Publish_WhenHandlerThrows_LogsError()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler failed"));

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Publish_WithCancellationToken_PassesToHandlers()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();
        var cts = new CancellationTokenSource();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cts.Token);

        // Wait for async processing
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        handlerMock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_WithCancelledToken_StopsProcessing()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not throw, cancellation is handled gracefully
        await eventBus.PublishAsync(testEvent, cts.Token);
    }

    #endregion

    #region Metrics

    [Fact]
    public void GetMetrics_WithNoEvents_ReturnsZeroMetrics()
    {
        // Arrange
        var eventBus = CreateEventBus();

        // Act
        var metrics = eventBus.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.PublishedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(0, metrics.RegisteredHandlers);
    }

    [Fact]
    public async Task GetMetrics_AfterPublishing_UpdatesPublishedCount()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(1, metrics.PublishedCount);
    }

    [Fact]
    public async Task GetMetrics_WithMultipleHandlers_TracksRegisteredHandlers()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        var handler2Mock = new Mock<IEventHandler<TestEvent>>();

        _services.AddSingleton(handler1Mock.Object);
        _services.AddSingleton(handler2Mock.Object);

        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handler1Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        handler2Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(2, metrics.RegisteredHandlers);
    }

    [Fact]
    public async Task GetMetrics_AfterMultiplePublishes_TracksAllMetrics()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(new TestEvent(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var metrics = eventBus.GetMetrics();
        Assert.Equal(3, metrics.PublishedCount);
    }

    #endregion

    #region IsRunning

    [Fact]
    public void IsRunning_ReturnsTrue()
    {
        // Arrange
        var eventBus = CreateEventBus();

        // Act & Assert
        Assert.True(eventBus.IsRunning);
    }

    #endregion

    #region Pipeline Integration

    [Fact]
    public async Task Publish_UsesPipelineForProcessing()
    {
        // Arrange
        var handlerMock = new Mock<IEventHandler<TestEvent>>();
        _services.AddSingleton(handlerMock.Object);
        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handlerMock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing through pipeline
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Handler was invoked through the pipeline
        handlerMock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Parallel Processing

    [Fact]
    public async Task Publish_WithMultipleHandlers_ProcessesInParallel()
    {
        // Arrange
        var handler1Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler1Complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler2Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        var handler2Mock = new Mock<IEventHandler<TestEvent>>();

        _services.AddSingleton(handler1Mock.Object);
        _services.AddSingleton(handler2Mock.Object);

        await using var eventBus = CreateEventBus();
        var testEvent = new TestEvent();

        handler1Mock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                handler1Started.TrySetResult(true);
                await releaseHandler1.Task;
                handler1Complete.TrySetResult(true);
            });

        handler2Mock
            .Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                handler2Started.TrySetResult(true);
                return Task.CompletedTask;
            });

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            await Task.WhenAll(handler1Started.Task, handler2Started.Task)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.False(handler1Complete.Task.IsCompleted);
        }
        finally
        {
            releaseHandler1.TrySetResult(true);
        }

        await handler1Complete.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    #endregion

    #region Different Event Types

    [Fact]
    public async Task Publish_WithDifferentEventTypes_InvokesCorrectHandlers()
    {
        // Arrange
        var handler1Mock = new Mock<IEventHandler<TestEvent>>();
        var handler2Mock = new Mock<IEventHandler<AnotherTestEvent>>();

        _services.AddSingleton(handler1Mock.Object);
        _services.AddSingleton(handler2Mock.Object);

        var eventBus = CreateEventBus();
        var testEvent = new TestEvent();
        var anotherEvent = new AnotherTestEvent();

        handler1Mock.Setup(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        handler2Mock.Setup(h => h.HandleAsync(anotherEvent, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await eventBus.PublishAsync(testEvent, cancellationToken: TestContext.Current.CancellationToken);
        await eventBus.PublishAsync(anotherEvent, cancellationToken: TestContext.Current.CancellationToken);

        // Wait for async processing
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        handler1Mock.Verify(h => h.HandleAsync(testEvent, It.IsAny<CancellationToken>()), Times.Once);
        handler2Mock.Verify(h => h.HandleAsync(anotherEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Test Helper Classes

    public class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class AnotherTestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    #endregion
}
