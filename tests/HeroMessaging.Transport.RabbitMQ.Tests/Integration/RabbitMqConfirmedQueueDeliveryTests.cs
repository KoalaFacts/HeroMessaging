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
        var received = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await Transport.SubscribeAsync(TransportAddress.Queue(queue),
            async (message, context, cancellationToken) =>
            {
                await context.AcknowledgeAsync(cancellationToken);
                received.TrySetResult(message);
            }, cancellationToken: TestContext.Current.CancellationToken);
        var envelope = CreateTestEnvelope() with { MessageType = "TestMessage" };

        await Transport.SendConfirmedAsync(TransportAddress.Queue(queue), envelope, TestContext.Current.CancellationToken);

        var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(envelope.MessageType, delivered.MessageType);
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
