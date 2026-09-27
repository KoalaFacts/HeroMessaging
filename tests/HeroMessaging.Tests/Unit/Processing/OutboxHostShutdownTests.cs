using HeroMessaging.Abstractions;
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

[Trait("Category", "Unit")]
public sealed class OutboxHostShutdownTests
{
    [Fact]
    public async Task Deadline_CancelsCooperativeLocalHandlerWithoutConsumingRetry()
    {
        var storage = new InMemoryOutboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new OutboxOptions(), TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken handlerToken = default;
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEvent _, CancellationToken cancellationToken) =>
            {
                entered.TrySetResult();
                handlerToken = cancellationToken;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System);
        IOutboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var deadline = new CancellationTokenSource();
        var stopping = registered.StopAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(handlerToken.IsCancellationRequested);

        using var updateTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        updateTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (entry.Status != OutboxStatus.Pending)
            await Task.Delay(TimeSpan.FromMilliseconds(10), updateTimeout.Token);

        Assert.Equal(OutboxStatus.Pending, entry.Status);
        Assert.Equal(0, entry.RetryCount);
        messaging.Verify(service => service.PublishAsync(It.IsAny<IEvent>(), It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
    }

    [Fact]
    public async Task Deadline_DoesNotWaitForeverForUncooperativeLocalHandler()
    {
        var storage = new InMemoryOutboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new OutboxOptions(), TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEvent _, CancellationToken _) =>
            {
                entered.TrySetResult();
                await release.Task;
            });
        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        var processor = new OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System);
        IOutboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var deadline = new CancellationTokenSource();
        var stopping = registered.StopAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await ((IAsyncDisposable)processor).DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(OutboxStatus.Processing, entry.Status);
        release.TrySetResult();
        using var completionTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        completionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (entry.Status != OutboxStatus.Processed)
            await Task.Delay(TimeSpan.FromMilliseconds(10), completionTimeout.Token);
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
