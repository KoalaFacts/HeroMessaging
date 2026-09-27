using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class InboxHostLifecycleTests
{
    [Fact]
    public async Task DirectStartCancellationAlsoStopsCleanup()
    {
        var storage = new Mock<IInboxStorage>();
        storage.Setup(value => value.GetUnprocessedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var timeProvider = new FakeTimeProvider();
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new InboxProcessor(storage.Object, services, NullLogger<InboxProcessor>.Instance, timeProvider);
        using var lifetime = new CancellationTokenSource();

        await processor.StartAsync(lifetime.Token);
        await lifetime.CancelAsync();
        timeProvider.Advance(TimeSpan.FromHours(2));
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        storage.Verify(value => value.CleanupOldEntriesAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HostStartsPendingInboxAndSurvivesStartupTokenCancellation()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new InboxOptions(), TestContext.Current.CancellationToken);
        var messaging = new Mock<IHeroMessaging>();
        var services = new ServiceCollection();
        new HeroMessagingBuilder(services).WithInbox().Build();
        services.AddSingleton<IInboxStorage>(storage);
        services.AddSingleton(messaging.Object);
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var processor = provider.GetRequiredService<IInboxProcessor>();
        using var startup = new CancellationTokenSource();

        await hosted.StartAsync(startup.Token);
        await startup.CancelAsync();
        Assert.True(processor.IsRunning);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (entry!.Status != InboxStatus.Processed)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);

        await hosted.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(processor.IsRunning);
        messaging.Verify(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GracefulStopWaitsForInFlightMessage()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new InboxOptions(), TestContext.Current.CancellationToken);
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
        await using var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        IInboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopping = registered.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stopping.IsCompleted);
        release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Processed, entry!.Status);
    }

    [Fact]
    public async Task ShutdownDeadlineLeavesCanceledMessagePendingForRecovery()
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new InboxOptions(), TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEvent _, CancellationToken cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        IInboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var deadline = new CancellationTokenSource();
        var stopping = registered.StopAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await ((IAsyncDisposable)processor).DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (entry!.Status != InboxStatus.Pending)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        Assert.Null(entry.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDeadlineDoesNotWaitForUncooperativeHandlerDuringDisposal(bool failsAfterShutdown)
    {
        var storage = new InMemoryInboxStorage(TimeProvider.System);
        var entry = await storage.AddAsync(new TestEvent(), new InboxOptions(), TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(service => service.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEvent _, CancellationToken _) =>
            {
                entered.TrySetResult();
                await release.Task;
                if (failsAfterShutdown)
                    throw new ObjectDisposedException("handler dependency");
            });
        using var services = new ServiceCollection().AddSingleton(messaging.Object).BuildServiceProvider();
        var processor = new InboxProcessor(storage, services, NullLogger<InboxProcessor>.Instance, TimeProvider.System);
        IInboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        using var deadline = new CancellationTokenSource();
        var stopping = registered.StopAsync(deadline.Token);
        await deadline.CancelAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await ((IAsyncDisposable)processor).DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(InboxStatus.Processing, entry!.Status);
        release.TrySetResult();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var expectedStatus = failsAfterShutdown ? InboxStatus.Pending : InboxStatus.Processed;
        while (entry.Status != expectedStatus)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        Assert.Null(entry.Error);
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
