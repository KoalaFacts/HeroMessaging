using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
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
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new InspectableOutboxProcessor(storage.Object, services,
            new Mock<IConfirmedQueueTransport>().Object, new Mock<IMessageSerializer>().Object);

        Assert.Equal(TimeSpan.FromMilliseconds(25), processor.PollingDelay(hasWork: true));
        Assert.Equal(TimeSpan.FromSeconds(1), processor.PollingDelay(hasWork: false));
    }

    private sealed class InspectableOutboxProcessor(
        IOutboxStorage storage,
        IServiceProvider services,
        IMessageTransport transport,
        IMessageSerializer serializer)
        : OutboxProcessor(storage, services, NullLogger<OutboxProcessor>.Instance, TimeProvider.System,
            transport, serializer)
    {
        public TimeSpan PollingDelay(bool hasWork) => GetPollingDelay(hasWork);
    }
}
