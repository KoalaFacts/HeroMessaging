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

        var entry = await RunInboxAsync(services, new InboxTestEvent());

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

        var entry = await RunInboxAsync(services, new InboxTestCommand());

        Assert.Equal(InboxStatus.Processed, entry.Status);
        Assert.Null(entry.Error);
        Assert.Equal(["command"], ledger.Calls);
    }

    private static async Task<InboxEntry> RunInboxAsync(IServiceCollection services, Abstractions.Messages.IMessage message)
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

        foreach (var service in hosted)
            await service.StopAsync(TestContext.Current.CancellationToken);
        return entry;
    }

    public sealed class HandlerLedger
    {
        private readonly List<string> _calls = [];

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
