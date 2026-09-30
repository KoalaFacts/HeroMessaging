using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class EventBusSchedulingTests
{
    public sealed class Admission
    {
        [Fact]
        public async Task ExecutingDeliveryStillConsumesCapacity()
        {
            var entered = Signal();
            var release = Signal();
            var calls = 0;
            using var provider = Provider(async (_, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
            });
            await using var bus = Bus(provider, capacity: 1, parallelism: 4);
            try
            {
                await Await(bus.PublishAsync(new SchedulingEvent(0), TestContext.Current.CancellationToken));
                await Await(entered.Task);
                var pending = bus.PublishAsync(new SchedulingEvent(1), TestContext.Current.CancellationToken);
                Assert.False(pending.IsCompleted);
                Assert.Equal(1, Volatile.Read(ref calls));
                release.TrySetResult();
                await Await(pending);
            }
            finally { release.TrySetResult(); }
            await bus.DisposeAsync();
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task CancelledPendingPublicationIsNotDelivered()
        {
            var entered = Signal();
            var release = Signal();
            var calls = 0;
            using var provider = Provider(async (_, ct) =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            });
            await using var bus = Bus(provider, capacity: 1, parallelism: 1);
            using var cancellation = new CancellationTokenSource();
            try
            {
                await Await(bus.PublishAsync(new SchedulingEvent(0), TestContext.Current.CancellationToken));
                await Await(entered.Task);
                var pending = bus.PublishAsync(new SchedulingEvent(1), cancellation.Token);
                Assert.False(pending.IsCompleted);
                cancellation.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Await(pending));
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            finally { release.TrySetResult(); }
            await bus.DisposeAsync();
            Assert.Equal(1, calls);
        }
    }

    public sealed class Concurrency
    {
        [Theory]
        [InlineData(1, 1, 0)]
        [InlineData(2, 8, 2)]
        [InlineData(8, 2, 2)]
        public async Task MixedPublicationModesDeliverEveryRegistrationExactlyOnce(int capacity, int parallelism, int poolSize)
        {
            const int producers = 8;
            const int messagesPerProducer = 32;
            var calls = new int[producers * messagesPerProducer];
            using var provider = Provider(async (message, _) =>
            {
                await Task.Yield();
                Interlocked.Increment(ref calls[message.Sequence]);
            }, registrations: 3);
            await using var bus = Bus(provider, capacity, parallelism, poolSize);
            var start = Signal();
            var publications = Enumerable.Range(0, producers).Select(async producer =>
            {
                await start.Task.WaitAsync(TestContext.Current.CancellationToken);
                for (var index = 0; index < messagesPerProducer; index++)
                {
                    var message = new SchedulingEvent(producer * messagesPerProducer + index);
                    if ((index & 1) == 0)
                        await bus.PublishAsync(message, TestContext.Current.CancellationToken);
                    else
                    {
                        var receipt = await bus.PublishAndWaitAsync(message, TestContext.Current.CancellationToken);
                        Assert.Equal(message.MessageId, receipt.MessageId);
                        Assert.True(receipt.Success);
                        Assert.Equal(3, receipt.Handlers.Count);
                    }
                }
            }).ToArray();
            start.TrySetResult();
            await Await(Task.WhenAll(publications));
            await bus.DisposeAsync();
            Assert.All(calls, count => Assert.Equal(3, count));
            Assert.Equal(calls.Length, bus.GetMetrics().PublishedCount);
        }

        [Fact]
        public async Task ParallelismLimitsActiveHandlersWithoutSerializingAllWork()
        {
            var entered = Signal();
            var release = Signal();
            var active = 0;
            var peak = 0;
            var calls = 0;
            using var provider = Provider(async (_, ct) =>
            {
                var current = Interlocked.Increment(ref active);
                int previous;
                do { previous = Volatile.Read(ref peak); }
                while (current > previous && Interlocked.CompareExchange(ref peak, current, previous) != previous);
                if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
                try { await release.Task.WaitAsync(ct); }
                finally { Interlocked.Decrement(ref active); }
            });
            await using var bus = Bus(provider, capacity: 8, parallelism: 2);
            try
            {
                for (var index = 0; index < 8; index++)
                    await Await(bus.PublishAsync(new SchedulingEvent(index), TestContext.Current.CancellationToken));
                await Await(entered.Task);
                Assert.Equal(2, Volatile.Read(ref active));
                Assert.Equal(2, Volatile.Read(ref calls));
            }
            finally { release.TrySetResult(); }
            await bus.DisposeAsync();
            Assert.Equal(8, calls);
            Assert.Equal(2, peak);
        }
    }

    public sealed class Shutdown
    {
        [Fact]
        public async Task DrainsQueuedReceiptsButRejectsPendingAdmission()
        {
            var entered = Signal();
            var release = Signal();
            var calls = 0;
            using var provider = Provider(async (_, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
            });
            await using var bus = Bus(provider, capacity: 2, parallelism: 1);
            var first = bus.PublishAndWaitAsync(new SchedulingEvent(0), TestContext.Current.CancellationToken);
            try
            {
                await Await(entered.Task);
                var queued = bus.PublishAndWaitAsync(new SchedulingEvent(1), TestContext.Current.CancellationToken);
                var pending = bus.PublishAndWaitAsync(new SchedulingEvent(2), TestContext.Current.CancellationToken);
                Assert.False(pending.IsCompleted);
                var shutdown = bus.DisposeAsync().AsTask();
                Assert.False(shutdown.IsCompleted);
                release.TrySetResult();
                Assert.True((await Await(first)).Success);
                Assert.True((await Await(queued)).Success);
                var rejected = await Await(pending);
                Assert.Equal(EventHandlerStatus.Rejected, Assert.Single(rejected.Handlers).Status);
                await Await(shutdown);
            }
            finally { release.TrySetResult(); }
            Assert.Equal(2, calls);
        }
    }

    public sealed class DispatcherFailures
    {
        [Fact]
        public async Task FaultDiscardsQueuedWorkButLetsAlreadyExecutingWorkFinish()
        {
            var firstEntered = Signal();
            var secondEntered = Signal();
            var fail = Signal();
            var finish = Signal();
            var discarded = Signal();
            var failure = new InvalidOperationException("Dispatcher failure");
            var finished = false;
            var queue = new EventDispatchQueue<int>(3, 2, async item =>
            {
                if (item == 0)
                {
                    firstEntered.TrySetResult();
                    await fail.Task;
                    throw failure;
                }
                Assert.Equal(1, item);
                secondEntered.TrySetResult();
                await finish.Task;
                finished = true;
            }, item =>
            {
                Assert.Equal(2, item);
                discarded.TrySetResult();
            });
            try
            {
                Assert.True(await Await(queue.SendAsync(0, TestContext.Current.CancellationToken)));
                await Await(firstEntered.Task);
                Assert.True(await Await(queue.SendAsync(1, TestContext.Current.CancellationToken)));
                await Await(secondEntered.Task);
                Assert.True(await Await(queue.SendAsync(2, TestContext.Current.CancellationToken)));
                var pending = queue.SendAsync(3, TestContext.Current.CancellationToken);
                Assert.False(pending.IsCompleted);
                fail.TrySetResult();
                await Await(discarded.Task);
                Assert.False(await Await(pending));
                Assert.False(finished);
                Assert.False(queue.Completion.IsCompleted);
            }
            finally
            {
                fail.TrySetResult();
                finish.TrySetResult();
                queue.Complete();
            }
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Await(queue.Completion));
            Assert.Same(failure, error);
            Assert.True(finished);
        }

        [Fact]
        public async Task CooperativeCancellationDoesNotFaultSubsequentDeliveries()
        {
            var finished = Signal();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var queue = new EventDispatchQueue<int>(1, 1, item =>
            {
                if (item == 0) return Task.FromCanceled(cancellation.Token);
                finished.TrySetResult();
                return Task.CompletedTask;
            }, _ => Assert.Fail("Cooperative cancellation must not discard queued deliveries."));
            try
            {
                Assert.True(await Await(queue.SendAsync(0, TestContext.Current.CancellationToken)));
                Assert.True(await Await(queue.SendAsync(1, TestContext.Current.CancellationToken)));
                await Await(finished.Task);
            }
            finally { queue.Complete(); }
            await Await(queue.Completion);
        }
    }

    private static ServiceProvider Provider(Func<SchedulingEvent, CancellationToken, Task> callback, int registrations = 1)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        for (var index = 0; index < registrations; index++)
            services.AddSingleton<IEventHandler<SchedulingEvent>>(new Handler(callback));
        return services.BuildServiceProvider();
    }

    private static EventBus Bus(IServiceProvider provider, int capacity, int parallelism, int poolSize = 2)
        => new(provider, null, new EventBusOptions
        {
            BoundedCapacity = capacity,
            MaxDegreeOfParallelism = parallelism,
            MaxPooledEnvelopes = poolSize
        });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    private static Task<T> Await<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private sealed class Handler(Func<SchedulingEvent, CancellationToken, Task> callback) : IEventHandler<SchedulingEvent>
    {
        public Task HandleAsync(SchedulingEvent message, CancellationToken cancellationToken = default)
            => callback(message, cancellationToken);
    }

    private sealed class SchedulingEvent(int sequence) : IEvent
    {
        public int Sequence { get; } = sequence;
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
