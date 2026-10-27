using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    private sealed class TestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
