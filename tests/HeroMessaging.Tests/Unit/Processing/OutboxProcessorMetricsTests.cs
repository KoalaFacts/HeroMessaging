using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// R14 step 2: <see cref="OutboxProcessor.GetMetrics"/> reports the deliveries this processor actually made.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboxProcessorMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LocalEntryDispatched_CountsProcessedAndRecordsTime()
    {
        var storage = LocalStorageReturningOnce(out var entry);
        storage.Setup(s => s.MarkProcessedAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEvent @event, CancellationToken _) =>
                new EventPublishReceipt(@event.MessageId, [new EventHandlerReceipt(typeof(string), EventHandlerStatus.Succeeded)]));

        var metrics = await RunLocalUntilAsync(storage, messaging, m => m.ProcessedMessages == 1);

        Assert.Equal(1, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.FailedMessages);
        Assert.Equal(Now, metrics.LastProcessedTime);
    }

    [Fact]
    public async Task LocalEntryDispatchThrows_CountsFailedAttemptAndNotProcessed()
    {
        var storage = LocalStorageReturningOnce(out var entry);
        storage.Setup(s => s.UpdateRetryCountAsync(entry.Id, 1, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var messaging = new Mock<IHeroMessaging>();
        messaging.Setup(m => m.PublishAndWaitAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler failed"));

        var metrics = await RunLocalUntilAsync(storage, messaging, m => m.FailedMessages == 1);

        Assert.Equal(1, metrics.FailedMessages);
        Assert.Equal(0, metrics.ProcessedMessages);
        Assert.Null(metrics.LastProcessedTime);
        storage.Verify(s => s.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExternalEntryConfirmedAndCompleted_CountsProcessed()
    {
        var storage = ExternalStorageClaimingOnce(out var entry, out var token);
        storage.Setup(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var transport = ConnectedTransport();
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = await RunExternalUntilAsync(storage, transport, m => m.ProcessedMessages == 1);

        Assert.Equal(1, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.FailedMessages);
        Assert.Equal(Now, metrics.LastProcessedTime);
    }

    [Fact]
    public async Task ExternalSendFails_CountsFailedAttemptAndNotProcessed()
    {
        var storage = ExternalStorageClaimingOnce(out var entry, out var token);
        storage.Setup(s => s.RetryExternalAsync(entry.Id, token, 1, It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var transport = ConnectedTransport();
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        var metrics = await RunExternalUntilAsync(storage, transport, m => m.FailedMessages == 1);

        Assert.Equal(1, metrics.FailedMessages);
        Assert.Equal(0, metrics.ProcessedMessages);
        Assert.Null(metrics.LastProcessedTime);
    }

    [Fact]
    public async Task ExternalLeaseLostAfterSend_IsNotCountedAsProcessed()
    {
        var storage = ExternalStorageClaimingOnce(out var entry, out var token);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.Setup(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult())
            .ReturnsAsync(false);
        var transport = ConnectedTransport();
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateExternalProcessor(storage, transport, services);
        await processor.StartAsync(TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        var metrics = processor.GetMetrics();
        Assert.Equal(0, metrics.ProcessedMessages);
        Assert.Equal(0, metrics.FailedMessages);
        Assert.Null(metrics.LastProcessedTime);
    }

    private static Mock<IOutboxStorage> LocalStorageReturningOnce(out OutboxEntry entry)
    {
        var pending = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = new MetricsTestEvent(),
            Status = OutboxStatus.Pending,
            Options = new OutboxOptions { MaxRetries = 3 }
        };
        var polls = 0;
        var storage = new Mock<IOutboxStorage>();
        storage.Setup(s => s.GetPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref polls) == 1 ? [pending] : []);
        entry = pending;
        return storage;
    }

    private static async Task<IOutboxProcessorMetrics> RunLocalUntilAsync(
        Mock<IOutboxStorage> storage, Mock<IHeroMessaging> messaging, Func<IOutboxProcessorMetrics, bool> done)
    {
        var services = new ServiceCollection();
        services.AddSingleton(messaging.Object);
        await using var provider = services.BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage.Object, provider, NullLogger<OutboxProcessor>.Instance,
            new FakeTimeProvider(Now));
        await processor.StartAsync(TestContext.Current.CancellationToken);
        var metrics = await WaitForAsync(processor, done);
        await processor.StopAsync(TestContext.Current.CancellationToken);
        return metrics;
    }

    private static Mock<IExternalOutboxStorage> ExternalStorageClaimingOnce(out OutboxEntry entry, out Guid token)
    {
        var claimed = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage("metrics"),
            Options = new OutboxOptions { Destination = "orders", MaxRetries = 3, RetryDelay = TimeSpan.FromSeconds(7) }
        };
        var lease = Guid.NewGuid();
        var claims = 0;
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claims) == 1 ? [new OutboxLease(claimed, lease)] : []);
        storage.Setup(s => s.RenewExternalAsync(claimed.Id, lease, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        entry = claimed;
        token = lease;
        return storage;
    }

    private static Mock<IConfirmedQueueTransport> ConnectedTransport()
    {
        var transport = new Mock<IConfirmedQueueTransport>();
        transport.SetupGet(t => t.State).Returns(TransportState.Connected);
        return transport;
    }

    private static OutboxProcessor CreateExternalProcessor(
        Mock<IExternalOutboxStorage> storage, Mock<IConfirmedQueueTransport> transport, IServiceProvider services)
    {
        var serializer = new Mock<IMessageSerializer>();
        serializer.SetupGet(s => s.ContentType).Returns("application/json");
        serializer.Setup(s => s.SerializeAsync(It.IsAny<TestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<byte[]>("{}"u8.ToArray()));
        return new OutboxProcessor(storage.Object, services, NullLogger<OutboxProcessor>.Instance,
            new FakeTimeProvider(Now), transport.Object, serializer.Object);
    }

    private static async Task<IOutboxProcessorMetrics> RunExternalUntilAsync(
        Mock<IExternalOutboxStorage> storage, Mock<IConfirmedQueueTransport> transport, Func<IOutboxProcessorMetrics, bool> done)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateExternalProcessor(storage, transport, services);
        await processor.StartAsync(TestContext.Current.CancellationToken);
        var metrics = await WaitForAsync(processor, done);
        await processor.StopAsync(TestContext.Current.CancellationToken);
        return metrics;
    }

    private static async Task<IOutboxProcessorMetrics> WaitForAsync(OutboxProcessor processor, Func<IOutboxProcessorMetrics, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var metrics = processor.GetMetrics();
        while (!done(metrics) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            metrics = processor.GetMetrics();
        }
        return metrics;
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
