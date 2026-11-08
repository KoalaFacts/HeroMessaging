using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public sealed class MessageDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_EventWithEveryHandlerSucceeded_ReturnsTrue()
    {
        var @event = new TestEvent();
        var messaging = new Mock<IHeroMessaging>(MockBehavior.Strict);
        messaging.Setup(service => service.PublishAndWaitAsync(@event, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventPublishReceipt(@event.MessageId, [new EventHandlerReceipt(typeof(string), EventHandlerStatus.Succeeded)]));

        var dispatched = await MessageDispatcher.DispatchAsync(messaging.Object, @event, source: "inbox", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(dispatched);
    }

    [Fact]
    public async Task DispatchAsync_EventHandlerFailed_ThrowsWithHandlerAndCause()
    {
        var @event = new TestEvent();
        var cause = new InvalidOperationException("stock unavailable");
        var messaging = new Mock<IHeroMessaging>(MockBehavior.Strict);
        messaging.Setup(service => service.PublishAndWaitAsync(@event, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventPublishReceipt(@event.MessageId,
            [
                new EventHandlerReceipt(typeof(string), EventHandlerStatus.Succeeded),
                new EventHandlerReceipt(typeof(Uri), EventHandlerStatus.Failed, ProcessingResult.Failed(cause)),
            ]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MessageDispatcher.DispatchAsync(messaging.Object, @event, source: "outbox", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(cause, exception.InnerException);
        Assert.Contains("[HM-DISPATCH-EVENT-HANDLER]", exception.Message);
        Assert.Contains("Uri: Failed (stock unavailable)", exception.Message);
        Assert.DoesNotContain("String", exception.Message);
        Assert.Contains("outbox", exception.Message);
        Assert.Contains(@event.MessageId.ToString(), exception.Message);
    }

    [Fact]
    public async Task DispatchAsync_EventHandlerRejectedWithoutException_ThrowsNamingTheStatus()
    {
        var @event = new TestEvent();
        var messaging = new Mock<IHeroMessaging>(MockBehavior.Strict);
        messaging.Setup(service => service.PublishAndWaitAsync(@event, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventPublishReceipt(@event.MessageId, [new EventHandlerReceipt(typeof(Uri), EventHandlerStatus.Rejected)]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MessageDispatcher.DispatchAsync(messaging.Object, @event, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(exception.InnerException);
        Assert.Contains("Uri: Rejected.", exception.Message);
        Assert.Contains("from processor", exception.Message);
    }

    public sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }
}
