using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// R5 step 5 (F08): a transient handler failure must not permanently fail a durable Inbox entry.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InboxTransientFailureTests
{
    [Fact]
    public async Task ProcessWorkItem_TransientHandlerFailureThenSuccess_EntryEndsProcessed()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new TimeoutException("database timeout"))
                : Task.CompletedTask);

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var command = new InboxProcessorTests.TestCommand();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(command, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, command.MessageId.ToString(), InboxStatus.Processed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Processed, entry?.Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProcessWorkItem_PermanentHandlerFailure_EntryEndsFailedAfterOneAttempt()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException(new ArgumentException("invalid order"));
            });

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var command = new InboxProcessorTests.TestCommand();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(command, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, command.MessageId.ToString(), InboxStatus.Failed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Failed, entry?.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProcessWorkItem_PermanentFailureWrappingTransientCause_EntryEndsFailedAfterOneAttempt()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException(new InvalidOperationException("order already shipped", new TimeoutException("lookup timed out")));
            });

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var command = new InboxProcessorTests.TestCommand();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(command, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, command.MessageId.ToString(), InboxStatus.Failed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Failed, entry?.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProcessWorkItem_TransientFailureEveryAttempt_EntryEndsFailedAfterMaxAttempts()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException(new TimeoutException("database timeout"));
            });

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var command = new InboxProcessorTests.TestCommand();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(command, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, command.MessageId.ToString(), InboxStatus.Failed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Failed, entry?.Status);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task PruneStaleTransientFailures_EntryRetriedLongAgo_IsForgotten()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.SendAsync(It.IsAny<ICommand>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromException(new TimeoutException("database timeout")));

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var command = new InboxProcessorTests.TestCommand();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(command, cancellationToken: TestContext.Current.CancellationToken));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (processor.TransientFailureCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, processor.TransientFailureCount);
        processor.PruneStaleTransientFailures(DateTimeOffset.UtcNow - TimeSpan.FromHours(1));
        Assert.Equal(1, processor.TransientFailureCount);
        processor.PruneStaleTransientFailures(DateTimeOffset.UtcNow + TimeSpan.FromHours(1));
        Assert.Equal(0, processor.TransientFailureCount);
    }

    [Fact]
    public async Task ProcessWorkItem_EventHandlerTransientFailureThenSuccess_EntryEndsProcessed()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEvent @event, CancellationToken _) => Interlocked.Increment(ref calls) == 1
                ? new EventPublishReceipt(@event.MessageId,
                    [new EventHandlerReceipt(typeof(string), EventHandlerStatus.Failed, ProcessingResult.Failed(new TimeoutException("database timeout")))])
                : new EventPublishReceipt(@event.MessageId, [new EventHandlerReceipt(typeof(string), EventHandlerStatus.Succeeded)]));

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var @event = new TransientTestEvent();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(@event, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, @event.MessageId.ToString(), InboxStatus.Processed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Processed, entry?.Status);
        Assert.Equal(2, calls);
        Assert.Equal(1, processor.GetMetrics().ProcessedMessages);
        Assert.Equal(0, processor.GetMetrics().FailedMessages);
    }

    [Fact]
    public async Task ProcessWorkItem_EventHandlerPermanentFailure_EntryEndsFailedAfterOneAttempt()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var calls = 0;
        var messaging = new Mock<IHeroMessaging>();
        messaging
            .Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEvent @event, CancellationToken _) =>
            {
                Interlocked.Increment(ref calls);
                return new EventPublishReceipt(@event.MessageId,
                    [new EventHandlerReceipt(typeof(string), EventHandlerStatus.Failed, ProcessingResult.Failed(new ArgumentException("invalid order")))]);
            });

        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        var @event = new TransientTestEvent();

        await processor.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(await processor.ProcessIncomingAsync(@event, cancellationToken: TestContext.Current.CancellationToken));

        var entry = await WaitForTerminalAsync(storage, @event.MessageId.ToString(), InboxStatus.Failed, TimeSpan.FromSeconds(20));
        await processor.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Failed, entry?.Status);
        Assert.Equal(1, calls);
        Assert.Equal(1, processor.GetMetrics().FailedMessages);
    }

    private sealed record TransientTestEvent : IEvent
    {
        public Guid MessageId { get; init; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; init; }
        public string? CausationId { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    private static async Task<InboxEntry?> WaitForTerminalAsync(IInboxStorage storage, string id, InboxStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        InboxEntry? entry;
        do
        {
            entry = await storage.GetAsync(id, TestContext.Current.CancellationToken);
            if (entry?.Status == expected)
                return entry;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        while (DateTime.UtcNow < deadline);
        return entry;
    }
}
