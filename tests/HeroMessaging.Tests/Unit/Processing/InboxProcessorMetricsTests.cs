using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class InboxProcessorMetricsTests : IAsyncDisposable
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);

    private readonly Mock<IInboxStorage> _storage = new();
    private readonly Mock<IHeroMessaging> _messaging = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly InboxProcessor _processor;

    public InboxProcessorMetricsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_messaging.Object);
        _serviceProvider = services.BuildServiceProvider();
        _processor = new InboxProcessor(_storage.Object, _serviceProvider, NullLogger<InboxProcessor>.Instance, TimeProvider.System);

        _storage
            .Setup(s => s.AddAsync(It.IsAny<IMessage>(), It.IsAny<InboxOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IMessage message, InboxOptions options, CancellationToken _) => new InboxEntry
            {
                Id = message.MessageId.ToString(),
                Message = message,
                Options = options,
                Status = InboxStatus.Pending
            });
        _messaging
            .Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEvent @event, CancellationToken _) => new EventPublishReceipt(@event.MessageId, []));
        _storage
            .Setup(s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storage
            .Setup(s => s.MarkFailedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public void GetMetrics_BeforeAnyMessage_ReportsZero()
    {
        var metrics = _processor.GetMetrics();

        Assert.Equal(0, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.DuplicateMessages);
        Assert.Equal(0, metrics.FailedMessages);
        Assert.Equal(0.0, metrics.DeduplicationRate);
    }

    [Fact]
    public async Task GetMetrics_AfterHandlerSucceeds_CountsProcessedMessages()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await _processor.ProcessIncomingAsync(new MetricsTestEvent(), cancellationToken: ct));
        Assert.True(await _processor.ProcessIncomingAsync(new MetricsTestEvent(), cancellationToken: ct));

        await WaitUntilAsync(() => _processor.GetMetrics().ProcessedMessages == 2, ct);
        var metrics = _processor.GetMetrics();
        Assert.Equal(2, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.FailedMessages);
        Assert.Equal(0, metrics.DuplicateMessages);
        Assert.Equal(0.0, metrics.DeduplicationRate);
    }

    [Fact]
    public async Task GetMetrics_AfterHandlerThrows_CountsFailedMessagesNotProcessed()
    {
        var ct = TestContext.Current.CancellationToken;
        _messaging
            .Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler failed"));

        Assert.True(await _processor.ProcessIncomingAsync(new MetricsTestEvent(), cancellationToken: ct));

        await WaitUntilAsync(() => _processor.GetMetrics().FailedMessages == 1, ct);
        var metrics = _processor.GetMetrics();
        Assert.Equal(1, metrics.FailedMessages);
        Assert.Equal(0, metrics.ProcessedMessages);
        _storage.Verify(s => s.MarkFailedAsync(It.IsAny<string>(), "handler failed", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMetrics_AfterDuplicates_CountsDuplicatesAndRateOverAllIncoming()
    {
        var ct = TestContext.Current.CancellationToken;
        var detectedDuplicate = new MetricsTestEvent();
        var rejectedOnInsert = new MetricsTestEvent();
        _storage
            .Setup(s => s.IsDuplicateAsync(detectedDuplicate.MessageId.ToString(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _storage
            .Setup(s => s.AddAsync(rejectedOnInsert, It.IsAny<InboxOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InboxEntry?)null);

        Assert.True(await _processor.ProcessIncomingAsync(new MetricsTestEvent(), cancellationToken: ct));
        Assert.False(await _processor.ProcessIncomingAsync(detectedDuplicate, cancellationToken: ct));
        Assert.False(await _processor.ProcessIncomingAsync(rejectedOnInsert, cancellationToken: ct));
        Assert.True(await _processor.ProcessIncomingAsync(new MetricsTestEvent(), cancellationToken: ct));

        var metrics = _processor.GetMetrics();
        Assert.Equal(2, metrics.DuplicateMessages);
        Assert.Equal(0.5, metrics.DeduplicationRate, 3);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitBudget;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                return;
            await Task.Delay(10, cancellationToken);
        }
    }

    public sealed class MetricsTestEvent : IEvent
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }
}
