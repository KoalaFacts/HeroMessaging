using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class LocalOutboxInFlightTests
{
    [Fact]
    public async Task RepeatedLocalPolls_WhileHandlerIsBlocked_DoNotDispatchTheSameEntryTwice()
    {
        var message = new TestCommand();
        var entryId = Guid.NewGuid().ToString();
        var pending = 1;
        var pollCount = 0;
        var dispatchCount = 0;
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repeatedPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var storage = new Mock<IOutboxStorage>();
        storage.Setup(value => value.GetPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (Interlocked.Increment(ref pollCount) >= 3)
                    repeatedPoll.TrySetResult();

                // Database reads return independent snapshots. Merely changing an
                // entry's in-memory Status does not claim its persisted row.
                return Volatile.Read(ref pending) == 1
                    ? [new OutboxEntry
                    {
                        Id = entryId,
                        Message = message,
                        Options = new OutboxOptions(),
                        Status = OutboxStatus.Pending
                    }]
                    : [];
            });
        storage.Setup(value => value.MarkProcessedAsync(entryId, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => Volatile.Write(ref pending, 0))
            .ReturnsAsync(true);

        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(value => value.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (ICommand _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref dispatchCount) == 1)
                {
                    handlerEntered.TrySetResult();
                    await releaseHandler.Task;
                }
            });

        using var services = new ServiceCollection()
            .AddSingleton(messaging.Object)
            .BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage.Object, services,
            NullLogger<OutboxProcessor>.Instance, TimeProvider.System);

        await processor.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await repeatedPoll.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await processor.StopAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, Volatile.Read(ref dispatchCount));
        storage.Verify(value => value.MarkProcessedAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedExternalClaim_DoesNotStrandLocalPendingEntry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var entryId = Guid.NewGuid().ToString();
        var command = new TestCommand();
        var claimCalls = 0;
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(true);
        storage.Setup(value => value.GetLocalPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [new OutboxEntry
            {
                Id = entryId,
                Message = command,
                Options = new OutboxOptions(),
                Status = OutboxStatus.Pending
            }]);
        storage.Setup(value => value.ClaimExternalAsync(1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref claimCalls) == 1)
                    return Task.FromException<IReadOnlyList<OutboxLease>>(
                        new InvalidOperationException("Transient external claim failure"));

                return Task.FromResult<IReadOnlyList<OutboxLease>>([]);
            });

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new PollableOutboxProcessor(storage.Object, services, clock);

        // The first local row was reserved, but the external claim failed before
        // the polling loop could submit any work to its handler.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.PollOnceAsync(TestContext.Current.CancellationToken));

        clock.Advance(TimeSpan.FromMilliseconds(101));
        var recovered = await processor.PollOnceAsync(TestContext.Current.CancellationToken);

        // The unchanged Pending row must still be eligible for real processing.
        Assert.Equal(entryId, Assert.Single(recovered).Entry.Id);
        storage.Verify(value => value.GetLocalPendingAsync(100, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(2, Volatile.Read(ref claimCalls));
    }

    [Fact]
    public async Task HighPriorityPublish_WhilePollingReadsTheSamePendingRow_DispatchesOnlyOnce()
    {
        var message = new TestCommand();
        var options = new OutboxOptions { Priority = 10 };
        var entryId = Guid.NewGuid().ToString();
        var pending = 1;
        var pollCount = 0;
        var dispatchCount = 0;
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repeatedPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var storage = new Mock<IOutboxStorage>();
        storage.Setup(value => value.AddAsync(message, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboxEntry
            {
                Id = entryId,
                Message = message,
                Options = options,
                Status = OutboxStatus.Pending
            });
        storage.Setup(value => value.GetPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (Interlocked.Increment(ref pollCount) >= 3)
                    repeatedPoll.TrySetResult();

                // Each poll sees a fresh persisted snapshot while the fast-path
                // handler remains blocked; no in-memory Status change can hide it.
                return Volatile.Read(ref pending) == 1
                    ? [new OutboxEntry
                    {
                        Id = entryId,
                        Message = message,
                        Options = options,
                        Status = OutboxStatus.Pending
                    }]
                    : [];
            });
        storage.Setup(value => value.MarkProcessedAsync(entryId, It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => Volatile.Write(ref pending, 0))
            .ReturnsAsync(true);

        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(value => value.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (ICommand _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref dispatchCount) == 1)
                {
                    handlerEntered.TrySetResult();
                    await releaseHandler.Task;
                }
            });

        using var services = new ServiceCollection()
            .AddSingleton(messaging.Object)
            .BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage.Object, services,
            NullLogger<OutboxProcessor>.Instance, TimeProvider.System);

        try
        {
            // Submit before starting the poller so the priority path owns the ID.
            await processor.PublishToOutboxAsync(message, options, TestContext.Current.CancellationToken);
            await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await processor.StartAsync(TestContext.Current.CancellationToken);
            await repeatedPoll.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await processor.StopAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, Volatile.Read(ref dispatchCount));
        storage.Verify(value => value.AddAsync(message, options, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(value => value.MarkProcessedAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HighPriorityPublish_AfterProcessorStops_DoesNotStrandPersistedLocalEntry()
    {
        var message = new TestCommand();
        var options = new OutboxOptions { Priority = 10 };
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options,
            Status = OutboxStatus.Pending
        };
        var pendingVisible = false;
        var storage = new Mock<IOutboxStorage>();
        storage.Setup(value => value.AddAsync(message, options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);
        storage.Setup(value => value.GetPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => pendingVisible
                ? [entry]
                : []);

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new PollableLocalOutboxProcessor(storage.Object, services);

        // Start with no pending rows, then complete the processing block.
        // An accepted durable write after shutdown cannot be queued in that block.
        await processor.StartAsync(TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        pendingVisible = true;
        await processor.PublishToOutboxAsync(message, options, TestContext.Current.CancellationToken);

        // Re-reading a persisted Pending row must be possible after an immediate
        // submission was declined. Returning false from SendAsync is not an exception.
        var retryable = await processor.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(entry.Id, Assert.Single(retryable).Entry.Id);
        storage.Verify(value => value.AddAsync(message, options, It.IsAny<CancellationToken>()), Times.Once);
    }


    [Fact]
    public async Task DeferredPendingEnumerationFailure_ReleasesReservedLocalEntry()
    {
        var entry = new OutboxEntry { Message = new TestCommand() };
        var reads = 0;
        var storage = new Mock<IOutboxStorage>();
        storage.Setup(value => value.GetPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref reads) == 1
                ? EnumerateThenFail(entry)
                : [entry]);

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new PollableLocalOutboxProcessor(storage.Object, services);

        // A storage implementation may return a deferred enumerable: its second
        // MoveNext can fail after an earlier row was already reserved locally.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.PollOnceAsync(TestContext.Current.CancellationToken));

        var retryable = await processor.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(entry.Id, Assert.Single(retryable).Entry.Id);
        Assert.Equal(2, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task DeferredExternalStorageLocalEnumerationFailure_ReleasesReservedLocalEntry()
    {
        var entry = new OutboxEntry { Message = new TestCommand() };
        var reads = 0;
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(false);
        storage.Setup(value => value.GetLocalPendingAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref reads) == 1
                ? EnumerateThenFail(entry)
                : [entry]);

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new PollableOutboxProcessor(storage.Object, services, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.PollOnceAsync(TestContext.Current.CancellationToken));

        var retryable = await processor.PollOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal(entry.Id, Assert.Single(retryable).Entry.Id);
        Assert.Equal(2, Volatile.Read(ref reads));
    }

    private static IEnumerable<OutboxEntry> EnumerateThenFail(OutboxEntry entry)
    {
        yield return entry;
        throw new InvalidOperationException("Deferred outbox enumeration failed");
    }

    private sealed class PollableLocalOutboxProcessor(
        IOutboxStorage storage, IServiceProvider services)
        : OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System)
    {
        public Task<IEnumerable<OutboxWorkItem>> PollOnceAsync(CancellationToken cancellationToken)
            => PollForWorkItemsAsync(cancellationToken);
    }

    private sealed class PollableOutboxProcessor(
        IExternalOutboxStorage storage, IServiceProvider services, TimeProvider timeProvider)
        : OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, timeProvider,
            new Mock<IConfirmedQueueTransport>().Object, new Mock<IMessageSerializer>().Object)
    {
        public Task<IEnumerable<OutboxWorkItem>> PollOnceAsync(CancellationToken cancellationToken)
            => PollForWorkItemsAsync(cancellationToken);
    }

    private sealed class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
