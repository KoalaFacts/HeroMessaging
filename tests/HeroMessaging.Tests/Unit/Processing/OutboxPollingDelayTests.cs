using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

public sealed class OutboxPollingDelayTests
{
    [Fact]
    public async Task ExternalDelivery_PollsPromptlyWhileBusyAndKeepsIdleDelay()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(true);
        storage.Setup(value => value.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var external = new OutboxLease(new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "benchmark" }
        }, Guid.NewGuid());
        storage.SetupSequence(value => value.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([external]);
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new InspectableOutboxProcessor(storage.Object, services,
            new Mock<IConfirmedQueueTransport>().Object, new Mock<IMessageSerializer>().Object);

        Assert.Equal(TimeSpan.FromMilliseconds(25), await processor.PollingDelayAsync());
        Assert.Equal(TimeSpan.FromMilliseconds(25), await processor.PollingDelayAsync());
        storage.Verify(value => value.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LocalWork_KeepsOriginalBusyDelayEvenWhenExternalClaimExists()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(true);
        var local = new OutboxEntry { Id = Guid.NewGuid().ToString(), Message = TestMessageBuilder.CreateValidMessage() };
        var external = new OutboxLease(new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "benchmark" }
        }, Guid.NewGuid());
        storage.Setup(value => value.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([local]);
        storage.Setup(value => value.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([external]);
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new InspectableOutboxProcessor(storage.Object, services,
            new Mock<IConfirmedQueueTransport>().Object, new Mock<IMessageSerializer>().Object);

        Assert.Equal(TimeSpan.FromMilliseconds(100), await processor.PollingDelayAsync());
    }

    [Fact]
    public async Task NoWork_KeepsOriginalIdleDelay()
    {
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(true);
        storage.Setup(value => value.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(value => value.ClaimExternalAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new InspectableOutboxProcessor(storage.Object, services,
            new Mock<IConfirmedQueueTransport>().Object, new Mock<IMessageSerializer>().Object);

        Assert.Equal(TimeSpan.FromSeconds(1), await processor.PollingDelayAsync());
    }

    private sealed class InspectableOutboxProcessor(
        IOutboxStorage storage,
        IServiceProvider services,
        IMessageTransport transport,
        IMessageSerializer serializer)
        : OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System,
            transport, serializer)
    {
        public async Task<TimeSpan> PollingDelayAsync()
        {
            var work = await PollForWorkItemsAsync(CancellationToken.None);
            return GetPollingDelay(work.Any());
        }
    }
}
