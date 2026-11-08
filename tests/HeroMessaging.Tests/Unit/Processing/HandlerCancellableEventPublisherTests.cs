using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// The wrappers between the persisted-message dispatcher and the event bus must forward the
/// handler-cancellation path, or the processor's stop token silently stops reaching handlers.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HandlerCancellableEventPublisherTests
{
    [Fact]
    public async Task TransactionEventBusDecorator_InnerSupportsHandlerCancellation_ForwardsEventAndToken()
    {
        var inner = new HandlerCancellableRecordingEventBus();
        IHandlerCancellableEventPublisher sut = CreateDecorator(inner);
        using var cts = new CancellationTokenSource();
        var @event = new TestEvent();

        var receipt = await sut.PublishAndWaitWithHandlerCancellationAsync(@event, cts.Token);

        Assert.Equal(nameof(IHandlerCancellableEventPublisher), inner.LastPath);
        Assert.Same(@event, inner.LastEvent);
        Assert.Equal(cts.Token, inner.LastToken);
        Assert.Equal(@event.MessageId, receipt.MessageId);
    }

    [Fact]
    public async Task TransactionEventBusDecorator_InnerWithoutHandlerCancellation_FallsBackToPublishAndWait()
    {
        var inner = new RecordingEventBus();
        IHandlerCancellableEventPublisher sut = CreateDecorator(inner);
        using var cts = new CancellationTokenSource();
        var @event = new TestEvent();

        await sut.PublishAndWaitWithHandlerCancellationAsync(@event, cts.Token);

        Assert.Equal(nameof(IEventBus.PublishAndWaitAsync), inner.LastPath);
        Assert.Same(@event, inner.LastEvent);
        Assert.Equal(cts.Token, inner.LastToken);
    }

    [Fact]
    public async Task HeroMessagingService_EventBusSupportsHandlerCancellation_ForwardsEventAndTokenAndCountsThePublish()
    {
        var bus = new HandlerCancellableRecordingEventBus();
        var service = CreateService(bus);
        using var cts = new CancellationTokenSource();
        var @event = new TestEvent();

        await ((IHandlerCancellableEventPublisher)service).PublishAndWaitWithHandlerCancellationAsync(@event, cts.Token);

        Assert.Equal(nameof(IHandlerCancellableEventPublisher), bus.LastPath);
        Assert.Same(@event, bus.LastEvent);
        Assert.Equal(cts.Token, bus.LastToken);
        Assert.Equal(1L, service.GetMetrics().EventsPublished);
    }

    [Fact]
    public async Task HeroMessagingService_EventBusWithoutHandlerCancellation_FallsBackToPublishAndWait()
    {
        var bus = new RecordingEventBus();
        var service = CreateService(bus);
        using var cts = new CancellationTokenSource();
        var @event = new TestEvent();

        await ((IHandlerCancellableEventPublisher)service).PublishAndWaitWithHandlerCancellationAsync(@event, cts.Token);

        Assert.Equal(nameof(IEventBus.PublishAndWaitAsync), bus.LastPath);
        Assert.Equal(cts.Token, bus.LastToken);
    }

    [Fact]
    public async Task HeroMessagingService_CancelledBeforePublish_ThrowsWithoutReachingTheBus()
    {
        var bus = new HandlerCancellableRecordingEventBus();
        var service = CreateService(bus);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ((IHandlerCancellableEventPublisher)service).PublishAndWaitWithHandlerCancellationAsync(new TestEvent(), cts.Token));

        Assert.Null(bus.LastPath);
    }

    private static TransactionEventBusDecorator CreateDecorator(IEventBus inner)
        => new(inner, Mock.Of<IUnitOfWorkFactory>(), NullLogger<TransactionEventBusDecorator>.Instance);

    private static HeroMessagingService CreateService(IEventBus bus)
        => new(Mock.Of<ICommandProcessor>(), Mock.Of<IQueryProcessor>(), bus, new FakeTimeProvider());

    private class RecordingEventBus : IEventBus
    {
        public string? LastPath { get; protected set; }
        public IEvent? LastEvent { get; protected set; }
        public CancellationToken LastToken { get; protected set; }

        public bool IsRunning => true;

        public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
            => Record(nameof(PublishAsync), @event, cancellationToken);

        public async Task<EventPublishReceipt> PublishAndWaitAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            await Record(nameof(PublishAndWaitAsync), @event, cancellationToken);
            return new EventPublishReceipt(@event.MessageId, []);
        }

        public IEventBusMetrics GetMetrics() => Mock.Of<IEventBusMetrics>();

        protected Task Record(string path, IEvent @event, CancellationToken cancellationToken)
        {
            LastPath = path;
            LastEvent = @event;
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerCancellableRecordingEventBus : RecordingEventBus, IHandlerCancellableEventPublisher
    {
        public async Task<EventPublishReceipt> PublishAndWaitWithHandlerCancellationAsync(IEvent @event, CancellationToken cancellationToken)
        {
            await Record(nameof(IHandlerCancellableEventPublisher), @event, cancellationToken);
            return new EventPublishReceipt(@event.MessageId, []);
        }
    }

    private sealed class TestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }
}
