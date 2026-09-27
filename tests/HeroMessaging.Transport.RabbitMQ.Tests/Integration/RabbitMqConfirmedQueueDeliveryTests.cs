using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class RabbitMqConfirmedQueueDeliveryTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task SendConfirmedAsync_DeclaredQueue_Succeeds()
    {
        var queue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);

        await Transport.SendConfirmedAsync(TransportAddress.Queue(queue), CreateTestEnvelope(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendConfirmedAsync_MissingQueue_RejectsUnroutableMessage()
    {
        var queue = CreateQueueName();

        var error = await Assert.ThrowsAsync<QueueDeliveryException>(() => Transport!.SendConfirmedAsync(
            TransportAddress.Queue(queue), CreateTestEnvelope(), TestContext.Current.CancellationToken));

        Assert.Equal(queue, error.Destination);
    }
}
