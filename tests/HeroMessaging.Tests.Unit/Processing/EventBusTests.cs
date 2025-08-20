using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Core.Processing;
using HeroMessaging.Tests.Unit.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HeroMessaging.Tests.Unit.Processing;

public class EventBusTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly EventBus _eventBus;
    private readonly Mock<ILogger<EventBus>> _loggerMock;

    public EventBusTests()
    {
        _loggerMock = new Mock<ILogger<EventBus>>();
        var services = new ServiceCollection();
        
        // Register multiple handlers for the same event
        services.AddSingleton<IEventHandler<UserCreatedEvent>, UserCreatedEmailHandler>();
        services.AddSingleton<IEventHandler<UserCreatedEvent>, UserCreatedAuditHandler>();
        services.AddSingleton<IEventHandler<UserCreatedEvent>, UserCreatedNotificationHandler>();
        services.AddSingleton<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler>();
        services.AddSingleton<IEventHandler<FailingEvent>, FailingEventHandler>();
        services.AddSingleton<IEventHandler<SlowEvent>, SlowEventHandler>();
        
        _serviceProvider = services.BuildServiceProvider();
        _eventBus = new EventBus(_serviceProvider, _loggerMock.Object);
    }

    [Fact]
    public async Task PublishAsync_ShouldNotifyAllHandlers()
    {
        // Arrange
        var @event = new UserCreatedEvent { UserId = "123", Username = "testuser" };
        UserCreatedEmailHandler.Reset();
        UserCreatedAuditHandler.Reset();
        UserCreatedNotificationHandler.Reset();

        // Act
        await _eventBus.Publish(@event);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.Single(UserCreatedEmailHandler.HandledEvents, e => e.UserId == "123");
        Assert.Single(UserCreatedAuditHandler.HandledEvents, e => e.UserId == "123");
        Assert.Single(UserCreatedNotificationHandler.HandledEvents, e => e.UserId == "123");
    }

    [Fact]
    public async Task PublishAsync_WithMultipleEvents_ShouldProcessInParallel()
    {
        // Arrange
        var events = Enumerable.Range(1, 10)
            .Select(i => new OrderPlacedEvent { OrderId = i.ToString() })
            .ToList();
        OrderPlacedHandler.Reset();

        // Act
        var tasks = events.Select(e => _eventBus.Publish(e));
        await Task.WhenAll(tasks);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.Equal(10, OrderPlacedHandler.HandledEvents.Count);
        var handledIds = OrderPlacedHandler.HandledEvents.Select(e => e.OrderId).OrderBy(id => id);
        var expectedIds = events.Select(e => e.OrderId).OrderBy(id => id);
        Assert.Equal(expectedIds, handledIds);
    }

    [Fact]
    public async Task PublishAsync_WhenOneHandlerFails_ShouldNotAffectOthers()
    {
        // Arrange
        var @event = new UserCreatedEvent { UserId = "456", Username = "failtest" };
        UserCreatedEmailHandler.Reset();
        UserCreatedEmailHandler.ShouldFail = true; // Make one handler fail
        UserCreatedAuditHandler.Reset();
        UserCreatedNotificationHandler.Reset();

        // Act
        await _eventBus.Publish(@event);
        await Task.Delay(100); // Allow async processing

        // Assert
        Assert.Empty(UserCreatedEmailHandler.HandledEvents); // Failed handler
        Assert.Single(UserCreatedAuditHandler.HandledEvents, e => e.UserId == "456");
        Assert.Single(UserCreatedNotificationHandler.HandledEvents, e => e.UserId == "456");
    }

    [Fact]
    public async Task PublishAsync_WithNullEvent_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _eventBus.Publish((UserCreatedEvent)null!));
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldCompleteWithoutError()
    {
        // Arrange
        var @event = new UnhandledEvent { Data = "test" };

        // Act & Assert (should not throw)
        await _eventBus.Publish(@event);
    }

    [Fact]
    public async Task PublishAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var @event = new SlowEvent { DelayMs = 5000 };
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<SlowEvent>, SlowEventHandler>();
        using var provider = services.BuildServiceProvider();
        var eventBus = new EventBus(provider, _loggerMock.Object);
        
        var cts = new CancellationTokenSource(100);

        // Act
        await eventBus.Publish(@event, cts.Token);
        await Task.Delay(200);

        // Assert
        Assert.Equal(0, SlowEventHandler.CompletedCount);
    }

    [Fact]
    public async Task Publish_ShouldHandleHighVolume()
    {
        // Arrange
        var eventCount = 1000;
        var events = Enumerable.Range(1, eventCount)
            .Select(i => new OrderPlacedEvent { OrderId = i.ToString() })
            .ToList();
        OrderPlacedHandler.Reset();

        // Act
        var tasks = events.Select(e => _eventBus.Publish(e));
        await Task.WhenAll(tasks);
        await Task.Delay(500); // Allow async processing

        // Assert
        Assert.Equal(eventCount, OrderPlacedHandler.HandledEvents.Count);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}