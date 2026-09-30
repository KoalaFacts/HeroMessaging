using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class EventPublishReceiptTests
{
    public sealed class SuccessfulDelivery
    {
        [Fact]
        public async Task NoHandlersReturnsAnEmptySuccessfulReceipt()
        {
            using var provider = CreateProvider();
            await using var bus = new EventBus(provider, null, null);
            var message = new ReceiptEvent();

            var receipt = await bus.PublishAndWaitAsync(message, TestContext.Current.CancellationToken);

            Assert.Equal(message.MessageId, receipt.MessageId);
            Assert.True(receipt.Success);
            Assert.Empty(receipt.Handlers);
        }

        [Fact]
        public async Task ReturnsFinalResultForEachHandlerRegistration()
        {
            var calls = 0;
            var handler = new CallbackHandler((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            });
            using var provider = CreateProvider(handler, handler);
            await using var bus = new EventBus(provider, null, null);

            var receipt = await bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);

            Assert.True(receipt.Success);
            Assert.Equal(2, calls);
            Assert.Equal(2, receipt.Handlers.Count);
            Assert.All(receipt.Handlers, item =>
            {
                Assert.Equal(typeof(CallbackHandler), item.HandlerType);
                Assert.Equal(EventHandlerStatus.Succeeded, item.Status);
                Assert.True(item.Result?.Success);
            });
        }

        [Fact]
        public async Task WaitsForEveryHandlerAndKeepsRegistrationOrder()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var handler = new CallbackHandler(async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            });
            var failed = new ArgumentException("Permanent handler failure");
            using var provider = CreateProvider(handler, new CallbackHandler((_, _) => Task.FromException(failed)));
            await using var bus = CreateBus(provider);
            var operation = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
            try
            {
                await Await(entered.Task);
                Assert.False(operation.IsCompleted);
            }
            finally { release.TrySetResult(); }

            var receipt = await Await(operation);
            Assert.False(receipt.Success);
            Assert.Equal(EventHandlerStatus.Succeeded, receipt.Handlers[0].Status);
            Assert.Equal(EventHandlerStatus.Failed, receipt.Handlers[1].Status);
            Assert.Same(failed, receipt.Handlers[1].Result?.Exception);
        }

        [Fact]
        public async Task OrdinaryPublishStillReturnsBeforeHandlerCompletion()
        {
            var entered = NewSignal();
            var release = NewSignal();
            using var provider = CreateProvider(new CallbackHandler(async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }));
            await using var bus = CreateBus(provider);
            try
            {
                await Await(bus.PublishAsync(new ReceiptEvent(), TestContext.Current.CancellationToken));
                await Await(entered.Task);
                Assert.False(release.Task.IsCompleted);
            }
            finally { release.TrySetResult(); }
        }

        [Fact]
        public async Task ConcurrentAndPooledDeliveriesKeepReceiptsIsolated()
        {
            var calls = 0;
            using var provider = CreateProvider(new CallbackHandler(async (_, _) =>
            {
                await Task.Yield();
                Interlocked.Increment(ref calls);
            }));
            await using var bus = CreateBus(provider);
            for (var round = 0; round < 3; round++)
            {
                var messages = Enumerable.Range(0, 32).Select(_ => new ReceiptEvent()).ToArray();
                var receipts = await Await(Task.WhenAll(messages.Select(message =>
                    bus.PublishAndWaitAsync(message, TestContext.Current.CancellationToken))));
                for (var index = 0; index < messages.Length; index++)
                {
                    Assert.Equal(messages[index].MessageId, receipts[index].MessageId);
                    Assert.True(receipts[index].Success);
                    Assert.Single(receipts[index].Handlers);
                }
                await bus.PublishAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
            }
            await bus.DisposeAsync();
            Assert.Equal(99, calls);
        }

        [Fact]
        public async Task ResolvesTransientHandlersForEachPublication()
        {
            var instances = 0;
            var calls = 0;
            using var provider = CreateProvider(services => services.AddTransient<IEventHandler<ReceiptEvent>>(_ =>
            {
                Interlocked.Increment(ref instances);
                return new CallbackHandler((_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    return Task.CompletedTask;
                });
            }));
            await using var bus = CreateBus(provider);

            for (var index = 0; index < 3; index++)
                Assert.True((await Await(bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken))).Success);

            Assert.Equal(3, instances);
            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task ShutdownDrainsAcceptedReceipts()
        {
            var entered = NewSignal();
            var release = NewSignal();
            using var provider = CreateProvider(new CallbackHandler(async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }));
            await using var bus = CreateBus(provider);
            var receipt = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
            Task shutdown;
            try
            {
                await Await(entered.Task);
                shutdown = bus.DisposeAsync().AsTask();
                Assert.False(shutdown.IsCompleted);
                Assert.False(receipt.IsCompleted);
            }
            finally { release.TrySetResult(); }

            Assert.True((await Await(receipt)).Success);
            await Await(shutdown);
        }
    }

    public sealed class FailedDelivery
    {
        [Fact]
        public async Task StoppedBusRejectsEveryHandlerWithoutExecutingIt()
        {
            var calls = 0;
            var handler = new CallbackHandler((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            });
            using var provider = CreateProvider(handler, handler);
            await using var bus = CreateBus(provider);
            await bus.DisposeAsync();

            var receipt = await Await(bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken));

            Assert.False(receipt.Success);
            Assert.Equal(0, calls);
            Assert.Equal(2, receipt.Handlers.Count);
            Assert.All(receipt.Handlers, outcome =>
            {
                Assert.Equal(EventHandlerStatus.Rejected, outcome.Status);
                Assert.Null(outcome.Result);
            });
        }

        [Fact]
        public async Task RetryReturnsOnlyTheFinalSuccessfulOutcome()
        {
            var attempts = 0;
            using var provider = CreateProvider(new CallbackHandler((_, _) =>
                Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException(new TimeoutException("Transient failure"))
                    : Task.CompletedTask));
            await using var bus = CreateBus(provider);

            var receipt = await Await(bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken));

            Assert.True(receipt.Success);
            Assert.Equal(2, attempts);
            Assert.Equal(EventHandlerStatus.Succeeded, Assert.Single(receipt.Handlers).Status);
        }

        [Fact]
        public async Task ValidationFailureIsReportedWithoutInvokingHandler()
        {
            var calls = 0;
            using var provider = CreateProvider(services => services.AddSingleton<IMessageValidator>(
                new CallbackValidator((_, _) => ValueTask.FromResult(ValidationResult.Failure("Invalid event")))),
                new CallbackHandler((_, _) =>
                {
                    calls++;
                    return Task.CompletedTask;
                }));
            await using var bus = CreateBus(provider);

            var receipt = await Await(bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken));

            Assert.False(receipt.Success);
            Assert.Equal(0, calls);
            var result = Assert.Single(receipt.Handlers);
            Assert.Equal(EventHandlerStatus.Failed, result.Status);
            Assert.IsType<ValidationException>(result.Result?.Exception);
        }

        [Fact]
        public async Task PipelineFaultFinishesActiveAndAbortsQueuedReceipts()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var failure = new InvalidOperationException("Validator infrastructure failed");
            using var provider = CreateProvider(services => services.AddSingleton<IMessageValidator>(
                new CallbackValidator(async (_, ct) =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                    throw failure;
                })), new CallbackHandler((_, _) => Task.CompletedTask));
            var bus = CreateBus(provider, capacity: 2, parallelism: 1);
            try
            {
                var active = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
                await Await(entered.Task);
                var queued = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
                Assert.False(queued.IsCompleted);
                var pending = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
                Assert.False(pending.IsCompleted);
                release.TrySetResult();

                var activeReceipt = await Await(active);
                var queuedReceipt = await Await(queued);
                Assert.Equal(EventHandlerStatus.Failed, Assert.Single(activeReceipt.Handlers).Status);
                Assert.Same(failure, activeReceipt.Handlers[0].Result?.Exception);
                Assert.Equal(EventHandlerStatus.Aborted, Assert.Single(queuedReceipt.Handlers).Status);
                Assert.Same(failure, queuedReceipt.Handlers[0].Result?.Exception);
                Assert.False(queuedReceipt.Success);

                var pendingReceipt = await Await(pending);
                Assert.Equal(EventHandlerStatus.Rejected, Assert.Single(pendingReceipt.Handlers).Status);

                var rejected = await Await(bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken));
                Assert.Equal(EventHandlerStatus.Rejected, Assert.Single(rejected.Handlers).Status);
            }
            finally
            {
                release.TrySetResult();
                await Assert.ThrowsAnyAsync<Exception>(() => bus.DisposeAsync().AsTask());
            }
        }
    }

    public sealed class CancelledWaiting
    {
        [Fact]
        public async Task AlreadyCancelledTokenPreventsPublication()
        {
            var calls = 0;
            using var provider = CreateProvider(new CallbackHandler((_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            }));
            await using var bus = CreateBus(provider);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                bus.PublishAndWaitAsync(new ReceiptEvent(), cancellation.Token));

            Assert.Equal(0, calls);
            Assert.Equal(0, bus.GetMetrics().PublishedCount);
        }

        [Fact]
        public async Task CancellationDoesNotCancelAcceptedHandlerExecution()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var finished = NewSignal();
            CancellationToken handlerToken = default;
            using var provider = CreateProvider(new CallbackHandler(async (_, ct) =>
            {
                handlerToken = ct;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
                finished.TrySetResult();
            }));
            await using var bus = CreateBus(provider);
            using var cancellation = new CancellationTokenSource();
            var operation = bus.PublishAndWaitAsync(new ReceiptEvent(), cancellation.Token);
            try
            {
                await Await(entered.Task);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Await(operation));
                Assert.False(handlerToken.CanBeCanceled);
                Assert.False(finished.Task.IsCompleted);
            }
            finally { release.TrySetResult(); }

            await Await(finished.Task);
        }

        [Fact]
        public async Task CancellationDoesNotAbandonPendingAdmission()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var secondDelivered = NewSignal();
            var calls = 0;
            using var provider = CreateProvider(new CallbackHandler(async (_, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }
                else secondDelivered.TrySetResult();
            }));
            await using var bus = CreateBus(provider, capacity: 1, parallelism: 1);
            using var cancellation = new CancellationTokenSource();
            var first = bus.PublishAndWaitAsync(new ReceiptEvent(), TestContext.Current.CancellationToken);
            try
            {
                await Await(entered.Task);
                var pending = bus.PublishAndWaitAsync(new ReceiptEvent(), cancellation.Token);
                Assert.False(pending.IsCompleted);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Await(pending));
                Assert.False(secondDelivered.Task.IsCompleted);
            }
            finally { release.TrySetResult(); }

            Assert.True((await Await(first)).Success);
            await Await(secondDelivered.Task);
            Assert.Equal(2, calls);
        }
    }

    public sealed class PublicContract
    {
        [Fact]
        public async Task NullEventIsRejected()
        {
            using var provider = CreateProvider();
            await using var bus = CreateBus(provider);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                bus.PublishAndWaitAsync(null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void ReceiptSnapshotsItsSourceAndCannotBeModified()
        {
            List<EventHandlerReceipt> outcomes =
                [new(typeof(CallbackHandler), EventHandlerStatus.Succeeded, ProcessingResult.Successful())];
            var receipt = new EventPublishReceipt(Guid.NewGuid(), outcomes);
            outcomes.Clear();

            Assert.True(receipt.Success);
            Assert.Single(receipt.Handlers);
            Assert.Throws<NotSupportedException>(() => ((IList<EventHandlerReceipt>)receipt.Handlers).Clear());
        }

        [Fact]
        public async Task FacadeForwardsReceiptAndUpdatesPublicationMetrics()
        {
            var inner = new Mock<IEventBus>(MockBehavior.Strict);
            var message = new ReceiptEvent();
            var token = TestContext.Current.CancellationToken;
            var receipt = new EventPublishReceipt(message.MessageId, []);
            inner.Setup(bus => bus.PublishAndWaitAsync(message, token)).ReturnsAsync(receipt);
            var service = CreateFacade(inner.Object);

            var result = await ((IEventPublisher)service).PublishAndWaitAsync(message, token);

            Assert.Same(receipt, result);
            Assert.Equal(1, service.GetMetrics().EventsPublished);
            inner.Verify(bus => bus.PublishAndWaitAsync(message, token), Times.Once);
        }

        [Fact]
        public async Task FacadeDoesNotPublishOrCountAnAlreadyCancelledCall()
        {
            var inner = new Mock<IEventBus>(MockBehavior.Strict);
            var service = CreateFacade(inner.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.PublishAndWaitAsync(new ReceiptEvent(), cancellation.Token));

            Assert.Equal(0, service.GetMetrics().EventsPublished);
            inner.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TransactionDecoratorForwardsTheReceipt()
        {
            var inner = new Mock<IEventBus>(MockBehavior.Strict);
            var message = new ReceiptEvent();
            var token = TestContext.Current.CancellationToken;
            var receipt = new EventPublishReceipt(message.MessageId, []);
            inner.Setup(bus => bus.PublishAndWaitAsync(message, token)).ReturnsAsync(receipt);
            var decorator = new TransactionEventBusDecorator(inner.Object,
                Mock.Of<IUnitOfWorkFactory>(), NullLogger<TransactionEventBusDecorator>.Instance);

            Assert.Same(receipt, await decorator.PublishAndWaitAsync(message, token));
            inner.Verify(bus => bus.PublishAndWaitAsync(message, token), Times.Once);
        }
    }

    private static ServiceProvider CreateProvider(params IEventHandler<ReceiptEvent>[] handlers)
        => CreateProvider(_ => { }, handlers);

    private static ServiceProvider CreateProvider(Action<ServiceCollection> configure, params IEventHandler<ReceiptEvent>[] handlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        foreach (var handler in handlers)
            services.AddSingleton(handler);
        configure(services);
        return services.BuildServiceProvider();
    }

    private static EventBus CreateBus(IServiceProvider provider, int capacity = 64, int parallelism = 2)
        => new(provider, null, new EventBusOptions { BoundedCapacity = capacity, MaxDegreeOfParallelism = parallelism });

    private static HeroMessagingService CreateFacade(IEventBus bus)
        => new(Mock.Of<ICommandProcessor>(), Mock.Of<IQueryProcessor>(), bus, TimeProvider.System);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private static Task<T> Await<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private sealed class CallbackValidator(Func<IMessage, CancellationToken, ValueTask<ValidationResult>> callback) : IMessageValidator
    {
        public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
            => callback(message, cancellationToken);
    }

    private sealed class CallbackHandler(Func<ReceiptEvent, CancellationToken, Task> callback) : IEventHandler<ReceiptEvent>
    {
        public Task HandleAsync(ReceiptEvent message, CancellationToken cancellationToken = default)
            => callback(message, cancellationToken);
    }

    private sealed class ReceiptEvent : IEvent
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
