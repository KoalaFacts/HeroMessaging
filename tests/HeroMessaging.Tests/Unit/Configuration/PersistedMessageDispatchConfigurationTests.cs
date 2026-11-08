using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// The Inbox, Outbox and queue processors hand persisted messages to local handlers through
/// <see cref="IHeroMessaging"/>, so enabling one of them must be enough for that dispatch to work.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PersistedMessageDispatchConfigurationTests
{
    public static TheoryData<string> PersistedMessagePatterns => ["inbox", "outbox", "queues"];

    [Theory]
    [MemberData(nameof(PersistedMessagePatterns))]
    public async Task PersistedMessagePattern_WithoutMediatorOrEventBus_ResolvesMessagingService(string pattern)
    {
        var services = new ServiceCollection().AddLogging();
        var builder = new HeroMessagingBuilder(services).UseInMemoryStorage();
        _ = pattern switch
        {
            "inbox" => builder.WithInbox(),
            "outbox" => builder.WithOutbox(),
            _ => builder.WithQueues(),
        };
        builder.Build();
        await using var provider = services.BuildServiceProvider();

        var messaging = provider.GetRequiredService<IHeroMessaging>();

        Assert.NotNull(messaging);
    }

    [Fact]
    public async Task WithInbox_WithoutMediatorOrEventBus_DeliversEventToItsHandler()
    {
        var ledger = new HandlerLedger();
        var services = new ServiceCollection().AddLogging().AddSingleton(ledger);
        services.AddTransient<IEventHandler<InboxTestEvent>, InboxTestEventHandler>();
        new HeroMessagingBuilder(services).UseInMemoryStorage().WithInbox().Build();

        var entry = await RunInboxAsync(services, ledger, new InboxTestEvent());

        Assert.Equal(InboxStatus.Processed, entry.Status);
        Assert.Null(entry.Error);
        Assert.Equal(["event"], ledger.Calls);
    }

    [Fact]
    public async Task Microservice_InboxCommand_ReachesItsCommandHandler()
    {
        var ledger = new HandlerLedger();
        var services = new ServiceCollection().AddLogging().AddSingleton(ledger);
        services.AddTransient<ICommandHandler<InboxTestCommand>, InboxTestCommandHandler>();
        new HeroMessagingBuilder(services).UseInMemoryStorage().Microservice("unused").Build();

        var entry = await RunInboxAsync(services, ledger, new InboxTestCommand());

        Assert.Equal(InboxStatus.Processed, entry.Status);
        Assert.Null(entry.Error);
        Assert.Equal(["command"], ledger.Calls);
    }

    [Fact]
    public async Task WithInbox_EventHandlerThrows_EntryIsNotMarkedProcessed()
    {
        var ledger = new HandlerLedger();
        var services = new ServiceCollection().AddLogging().AddSingleton(ledger);
        services.AddTransient<IEventHandler<InboxTestEvent>, ThrowingInboxTestEventHandler>();
        new HeroMessagingBuilder(services).UseInMemoryStorage().WithInbox().Build();

        var entry = await RunInboxAsync(services, ledger, new InboxTestEvent());

        Assert.Equal(["throwing-event"], ledger.Calls);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Contains(nameof(ThrowingInboxTestEventHandler), entry.Error);
        Assert.Contains("handler failed", entry.Error);
    }

    [Fact]
    public async Task WithInbox_EventHandlerStillRunning_EntryIsNotProcessedUntilItCompletes()
    {
        var gate = new BlockingHandlerGate();
        var services = new ServiceCollection().AddLogging().AddSingleton(gate);
        services.AddTransient<IEventHandler<InboxTestEvent>, BlockingInboxTestEventHandler>();
        new HeroMessagingBuilder(services).UseInMemoryStorage().WithInbox().Build();
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(TestContext.Current.CancellationToken);
        var inbox = provider.GetRequiredService<IInboxProcessor>();
        var storage = provider.GetRequiredService<IInboxStorage>();
        var message = new InboxTestEvent();

        Assert.True(await inbox.ProcessIncomingAsync(message, cancellationToken: TestContext.Current.CancellationToken));
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        // In-memory storage returns the live entry, so capture its status now.
        var statusWhileRunning = (await storage.GetAsync(message.MessageId.ToString(), TestContext.Current.CancellationToken))?.Status;

        gate.Release();
        InboxEntry? afterCompletion;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
                afterCompletion = await storage.GetAsync(message.MessageId.ToString(), timeout.Token);
            }
            while (afterCompletion is null || afterCompletion.Status != InboxStatus.Processed);
        }

        foreach (var service in hosted)
            await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(statusWhileRunning);
        Assert.NotEqual(InboxStatus.Processed, statusWhileRunning);
        Assert.Equal(InboxStatus.Processed, afterCompletion.Status);
    }

    public sealed class BlockingHandlerGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public Task WaitForReleaseAsync()
        {
            _entered.TrySetResult();
            return _released.Task;
        }

        public void Release() => _released.TrySetResult();
    }

    public sealed class BlockingInboxTestEventHandler(BlockingHandlerGate gate) : IEventHandler<InboxTestEvent>
    {
        public Task HandleAsync(InboxTestEvent @event, CancellationToken cancellationToken = default) =>
            gate.WaitForReleaseAsync();
    }

    public sealed class ThrowingInboxTestEventHandler(HandlerLedger ledger) : IEventHandler<InboxTestEvent>
    {
        public Task HandleAsync(InboxTestEvent @event, CancellationToken cancellationToken = default)
        {
            ledger.Record("throwing-event");
            throw new InvalidOperationException("handler failed");
        }
    }

    private static async Task<InboxEntry> RunInboxAsync(IServiceCollection services, HandlerLedger ledger, Abstractions.Messages.IMessage message)
    {
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
            await service.StartAsync(TestContext.Current.CancellationToken);

        var inbox = provider.GetRequiredService<IInboxProcessor>();
        var storage = provider.GetRequiredService<IInboxStorage>();
        Assert.True(await inbox.ProcessIncomingAsync(message, cancellationToken: TestContext.Current.CancellationToken));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        InboxEntry? entry;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            entry = await storage.GetAsync(message.MessageId.ToString(), timeout.Token);
        }
        while (entry is null || entry.Status is InboxStatus.Pending or InboxStatus.Processing);

        // Processed means the handler completed; the wait only guards the provider disposal below.
        if (entry.Status == InboxStatus.Processed)
            await ledger.FirstCall.WaitAsync(timeout.Token);

        foreach (var service in hosted)
            await service.StopAsync(TestContext.Current.CancellationToken);
        return entry;
    }

    public sealed class HandlerLedger
    {
        private readonly List<string> _calls = [];
        private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstCall => _firstCall.Task;

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls)
                    return [.. _calls];
            }
        }

        public void Record(string call)
        {
            lock (_calls)
                _calls.Add(call);
            _firstCall.TrySetResult();
        }
    }

    public sealed class InboxTestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public sealed class InboxTestCommand : ICommand
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public sealed class InboxTestEventHandler(HandlerLedger ledger) : IEventHandler<InboxTestEvent>
    {
        public Task HandleAsync(InboxTestEvent @event, CancellationToken cancellationToken = default)
        {
            ledger.Record("event");
            return Task.CompletedTask;
        }
    }

    public sealed class InboxTestCommandHandler(HandlerLedger ledger) : ICommandHandler<InboxTestCommand>
    {
        public Task HandleAsync(InboxTestCommand command, CancellationToken cancellationToken = default)
        {
            ledger.Record("command");
            return Task.CompletedTask;
        }
    }
}
