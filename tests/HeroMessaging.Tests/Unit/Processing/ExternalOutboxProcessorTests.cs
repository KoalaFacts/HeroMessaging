using HeroMessaging.Abstractions;
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

[Trait("Category", "Unit")]
public sealed class ExternalOutboxProcessorTests
{
    [Fact]
    public async Task PublishToOutbox_WithoutLeasedStorage_RejectsBeforeWrite()
    {
        var storage = new Mock<IOutboxStorage>();
        using var services = new ServiceCollection().BuildServiceProvider();
        var processor = new OutboxProcessor(storage.Object, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<NotSupportedException>(() => processor.PublishToOutboxAsync(
            TestMessageBuilder.CreateValidMessage(),
            new OutboxOptions { Destination = "orders" },
            TestContext.Current.CancellationToken));

        storage.Verify(s => s.AddAsync(It.IsAny<HeroMessaging.Abstractions.Messages.IMessage>(),
            It.IsAny<OutboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishToOutbox_WithSharedStorage_RejectsBeforeWrite()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        var transport = new Mock<IConfirmedQueueTransport>();
        using var services = new ServiceCollection().BuildServiceProvider();
        var processor = CreateProcessor(storage.Object, transport.Object, services);

        await Assert.ThrowsAsync<NotSupportedException>(() => processor.PublishToOutboxAsync(
            TestMessageBuilder.CreateValidMessage(),
            new OutboxOptions { Destination = "orders" },
            TestContext.Current.CancellationToken));

        storage.Verify(s => s.AddAsync(It.IsAny<HeroMessaging.Abstractions.Messages.IMessage>(),
            It.IsAny<OutboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SharedStorage_PollsLocalMessagesWithoutClaimingExternally()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => polled.TrySetResult())
            .ReturnsAsync([]);
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, new Mock<IConfirmedQueueTransport>().Object, services);

        await processor.StartAsync(TestContext.Current.CancellationToken);
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        storage.Verify(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingConfirmedTransport_DoesNotClaimExternally()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => polled.TrySetResult())
            .ReturnsAsync([]);
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage.Object, services, NullLogger<OutboxProcessor>.Instance,
            new FakeTimeProvider(DateTimeOffset.UtcNow));

        await processor.StartAsync(TestContext.Current.CancellationToken);
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        storage.Verify(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmedSend_CompletesClaimWithStableMessageId()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var transport = new Mock<IConfirmedQueueTransport>();
        var message = TestMessageBuilder.CreateValidMessage("external delivery");
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = new OutboxOptions { Destination = "orders" }
        };
        var token = Guid.NewGuid();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1 ? [new OutboxLease(entry, token)] : []);
        storage.Setup(s => s.RenewExternalAsync(entry.Id, token, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storage.Setup(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>()))
            .Callback(() => completed.TrySetResult())
            .ReturnsAsync(true);
        transport.SetupGet(t => t.State).Returns(TransportState.Connected);
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var serializer = new Mock<IMessageSerializer>();
        serializer.SetupGet(s => s.ContentType).Returns("application/custom");
        serializer.Setup(s => s.SerializeAsync(It.IsAny<TestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<byte[]>("custom-wire"u8.ToArray()));

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, transport.Object, services, serializer.Object);
        await processor.StartAsync(TestContext.Current.CancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        transport.Verify(t => t.SendConfirmedAsync(
            It.Is<TransportAddress>(address => address.Name == "orders" && address.Type == TransportAddressType.Queue),
            It.Is<TransportEnvelope>(envelope => envelope.MessageId == message.MessageId.ToString()
                && envelope.ContentType == "application/custom" && envelope.MessageType.Contains(nameof(TestMessage))
                && System.Text.Encoding.UTF8.GetString(envelope.Body.ToArray()) == "custom-wire"),
            It.IsAny<CancellationToken>()), Times.Once);
        serializer.Verify(s => s.SerializeAsync(It.Is<TestMessage>(value => ReferenceEquals(value, message)),
            It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedSend_RetriesWithoutCompletingClaim()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var transport = new Mock<IConfirmedQueueTransport>();
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "orders", RetryDelay = TimeSpan.FromSeconds(7) }
        };
        var token = Guid.NewGuid();
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1 ? [new OutboxLease(entry, token)] : []);
        storage.Setup(s => s.RenewExternalAsync(entry.Id, token, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storage.Setup(s => s.RetryExternalAsync(entry.Id, token, 1, It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => retried.TrySetResult())
            .ReturnsAsync(true);
        transport.SetupGet(t => t.State).Returns(TransportState.Connected);
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, transport.Object, services);
        await processor.StartAsync(TestContext.Current.CancellationToken);
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        storage.Verify(s => s.CompleteExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.RetryExternalAsync(entry.Id, token, 1,
            It.IsAny<DateTimeOffset>(), "broker unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShutdownDeadline_CancelsUnconfirmedSendAndMakesLeaseRetryable()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var transport = new Mock<IConfirmedQueueTransport>();
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "orders" }
        };
        var token = Guid.NewGuid();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken deliveryToken = default;
        var claimCount = 0;
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1 ? [new OutboxLease(entry, token)] : []);
        storage.Setup(s => s.RenewExternalAsync(entry.Id, token, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storage.Setup(s => s.RetryExternalAsync(entry.Id, token, 0, It.IsAny<DateTimeOffset>(),
                "Delivery interrupted by shutdown", It.IsAny<CancellationToken>()))
            .Callback(() => retried.TrySetResult())
            .ReturnsAsync(true);
        transport.SetupGet(t => t.State).Returns(TransportState.Connected);
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (TransportAddress _, TransportEnvelope _, CancellationToken cancellationToken) =>
            {
                sending.TrySetResult();
                deliveryToken = cancellationToken;
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    canceled.TrySetResult();
                }
            });

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, transport.Object, services);
        IOutboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var shutdown = new CancellationTokenSource();
        var stopping = registered.StopAsync(shutdown.Token);
        await shutdown.CancelAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(deliveryToken.IsCancellationRequested);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        storage.Verify(s => s.CompleteExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.FailExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GracefulShutdown_WaitsForConfirmedSendToComplete()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var transport = new Mock<IConfirmedQueueTransport>();
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "orders" }
        };
        var token = Guid.NewGuid();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1 ? [new OutboxLease(entry, token)] : []);
        storage.Setup(s => s.RenewExternalAsync(entry.Id, token, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storage.Setup(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        transport.SetupGet(t => t.State).Returns(TransportState.Connected);
        transport.Setup(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (TransportAddress _, TransportEnvelope _, CancellationToken cancellationToken) =>
            {
                sending.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            });

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, transport.Object, services);
        IOutboxProcessor registered = processor;
        await registered.StartAsync(TestContext.Current.CancellationToken);
        await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var stopping = registered.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stopping.IsCompleted);
        release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        storage.Verify(s => s.CompleteExternalAsync(entry.Id, token, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.RetryExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LostLeaseBeforeExecution_DoesNotSend()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(s => s.SupportsExternalClaims).Returns(true);
        var transport = new Mock<IConfirmedQueueTransport>();
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "orders" }
        };
        var token = Guid.NewGuid();
        var checkedLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;
        storage.Setup(s => s.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claimCount) == 1 ? [new OutboxLease(entry, token)] : []);
        storage.Setup(s => s.RenewExternalAsync(entry.Id, token, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => checkedLease.TrySetResult())
            .ReturnsAsync(false);

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = CreateProcessor(storage.Object, transport.Object, services);
        await processor.StartAsync(TestContext.Current.CancellationToken);
        await checkedLease.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await processor.StopAsync(TestContext.Current.CancellationToken);

        transport.Verify(t => t.SendConfirmedAsync(It.IsAny<TransportAddress>(), It.IsAny<TransportEnvelope>(),
            It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.CompleteExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OutboxProcessor CreateProcessor(
        IExternalOutboxStorage storage, IConfirmedQueueTransport transport, IServiceProvider services,
        IMessageSerializer? configuredSerializer = null)
    {
        var serializer = new Mock<IMessageSerializer>();
        serializer.SetupGet(s => s.ContentType).Returns("application/json");
        serializer.Setup(s => s.SerializeAsync(It.IsAny<TestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<byte[]>("{}"u8.ToArray()));
        return new OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance,
            new FakeTimeProvider(DateTimeOffset.UtcNow), transport, configuredSerializer ?? serializer.Object);
    }
}
