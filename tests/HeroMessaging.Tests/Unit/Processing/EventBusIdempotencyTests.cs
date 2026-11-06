using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class EventBusIdempotencyTests
{
    [Fact]
    public async Task PublishAndWaitAsync_WithIdempotency_RunsEachHandlerOncePerMessageId()
    {
        var counters = new Counters();
        using var provider = BuildProvider(counters, withIdempotency: true);
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var @event = new DuplicateEvent { MessageId = Guid.NewGuid() };

        var first = await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);
        var second = await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(1, counters.First);
        Assert.Equal(1, counters.Second);
        Assert.All(first.Handlers, receipt => Assert.Equal(EventHandlerStatus.Succeeded, receipt.Status));
        Assert.All(second.Handlers, receipt => Assert.Equal(EventHandlerStatus.Succeeded, receipt.Status));
    }

    [Fact]
    public async Task PublishAndWaitAsync_WithIdempotency_RunsHandlersForDistinctMessageIds()
    {
        var counters = new Counters();
        using var provider = BuildProvider(counters, withIdempotency: true);
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);

        await bus.PublishAndWaitAsync(new DuplicateEvent { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        await bus.PublishAndWaitAsync(new DuplicateEvent { MessageId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(2, counters.First);
        Assert.Equal(2, counters.Second);
    }

    [Fact]
    public async Task PublishAndWaitAsync_WithoutIdempotency_RunsHandlersForEveryDelivery()
    {
        var counters = new Counters();
        using var provider = BuildProvider(counters, withIdempotency: false);
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var @event = new DuplicateEvent { MessageId = Guid.NewGuid() };

        await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);
        await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(2, counters.First);
        Assert.Equal(2, counters.Second);
    }

    [Fact]
    public async Task PublishAndWaitAsync_WithIdempotency_ReturnsCachedIdempotentFailureWithoutRerunningHandler()
    {
        var counters = new Counters { FirstThrows = () => new ArgumentException("invalid order") };
        using var provider = BuildProvider(counters, withIdempotency: true);
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var @event = new DuplicateEvent { MessageId = Guid.NewGuid() };

        await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);
        var second = await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(1, counters.First);
        Assert.Equal(1, counters.Second);
        var failed = Assert.Single(second.Handlers, receipt => receipt.HandlerType == typeof(FirstHandler));
        Assert.Equal(EventHandlerStatus.Failed, failed.Status);
    }

    [Fact]
    public async Task PublishAndWaitAsync_WithIdempotency_RerunsHandlerAfterTransientFailure()
    {
        var calls = 0;
        var counters = new Counters
        {
            FirstThrows = () => Interlocked.Increment(ref calls) == 1 ? new IOException("disk busy") : null
        };
        using var provider = BuildProvider(counters, withIdempotency: true);
        await using var bus = new EventBus(provider, NullLogger<EventBus>.Instance);
        var @event = new DuplicateEvent { MessageId = Guid.NewGuid() };

        var first = await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);
        var second = await bus.PublishAndWaitAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(EventHandlerStatus.Failed,
            Assert.Single(first.Handlers, receipt => receipt.HandlerType == typeof(FirstHandler)).Status);
        Assert.Equal(EventHandlerStatus.Succeeded,
            Assert.Single(second.Handlers, receipt => receipt.HandlerType == typeof(FirstHandler)).Status);
        Assert.Equal(2, counters.First);
        Assert.Equal(1, counters.Second);
    }

    private static ServiceProvider BuildProvider(Counters counters, bool withIdempotency)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(counters);
        services.AddSingleton<IEventHandler<DuplicateEvent>, FirstHandler>();
        services.AddSingleton<IEventHandler<DuplicateEvent>, SecondHandler>();
        if (withIdempotency)
            services.AddHeroMessaging().WithIdempotency();
        return services.BuildServiceProvider();
    }

    public sealed record DuplicateEvent : IEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public sealed class Counters
    {
        private int _first;
        private int _second;
        public Func<Exception?>? FirstThrows { get; init; }
        public int First => Volatile.Read(ref _first);
        public int Second => Volatile.Read(ref _second);
        public void HitFirst() => Interlocked.Increment(ref _first);
        public void HitSecond() => Interlocked.Increment(ref _second);
    }

    public sealed class FirstHandler(Counters counters) : IEventHandler<DuplicateEvent>
    {
        public Task HandleAsync(DuplicateEvent @event, CancellationToken cancellationToken = default)
        {
            counters.HitFirst();
            var failure = counters.FirstThrows?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    public sealed class SecondHandler(Counters counters) : IEventHandler<DuplicateEvent>
    {
        public Task HandleAsync(DuplicateEvent @event, CancellationToken cancellationToken = default)
        {
            counters.HitSecond();
            return Task.CompletedTask;
        }
    }
}
