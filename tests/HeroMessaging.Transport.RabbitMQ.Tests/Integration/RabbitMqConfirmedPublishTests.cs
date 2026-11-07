using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class RabbitMqConfirmedPublishTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task PublishConfirmedAsync_MatchingBinding_DeliversToEveryBoundQueue()
    {
        var exchange = CreateExchangeName();
        var matching = CreateQueueName();
        var other = CreateQueueName();
        await ConfigureTopicAsync(exchange, (matching, "orders.created"), (other, "orders.*"));
        var first = await ReceiveOneAsync(matching);
        var second = await ReceiveOneAsync(other);
        var envelope = CreateTestEnvelope() with { MessageType = "OrderCreated" };
        envelope = envelope.WithHeader("RoutingKey", "orders.created");

        await Transport!.PublishConfirmedAsync(TransportAddress.Topic(exchange), envelope, TestContext.Current.CancellationToken);

        Assert.Equal(envelope.MessageId, (await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).MessageId);
        Assert.Equal(envelope.MessageId, (await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).MessageId);
    }

    [Fact]
    public async Task PublishConfirmedAsync_NoMatchingBinding_ThrowsPublishRouteException()
    {
        var exchange = CreateExchangeName();
        var queue = CreateQueueName();
        await ConfigureTopicAsync(exchange, (queue, "orders.created"));
        var envelope = CreateTestEnvelope().WithHeader("RoutingKey", "invoices.created");

        var error = await Assert.ThrowsAsync<PublishRouteException>(() => Transport!.PublishConfirmedAsync(
            TransportAddress.Topic(exchange), envelope, TestContext.Current.CancellationToken));

        Assert.Equal(exchange, error.Topic);
        Assert.Equal("invoices.created", error.RoutingKey);
        Assert.Contains(exchange, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishConfirmedAsync_ExchangeWithoutBindings_ThrowsPublishRouteException()
    {
        var exchange = CreateExchangeName();
        await ConfigureTopicAsync(exchange);

        var error = await Assert.ThrowsAsync<PublishRouteException>(() => Transport!.PublishConfirmedAsync(
            TransportAddress.Topic(exchange), CreateTestEnvelope(), TestContext.Current.CancellationToken));

        Assert.Equal("#", error.RoutingKey);
    }

    [Fact]
    public async Task PublishConfirmedAsync_AfterUnroutablePublish_NextPublishStillSucceeds()
    {
        var exchange = CreateExchangeName();
        var queue = CreateQueueName();
        await ConfigureTopicAsync(exchange, (queue, "orders.created"));
        var received = await ReceiveOneAsync(queue);
        await Assert.ThrowsAsync<PublishRouteException>(() => Transport!.PublishConfirmedAsync(
            TransportAddress.Topic(exchange), CreateTestEnvelope().WithHeader("RoutingKey", "nobody"), TestContext.Current.CancellationToken));
        var envelope = CreateTestEnvelope().WithHeader("RoutingKey", "orders.created");

        await Transport!.PublishConfirmedAsync(TransportAddress.Topic(exchange), envelope, TestContext.Current.CancellationToken);

        Assert.Equal(envelope.MessageId, (await received.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).MessageId);
    }

    private async Task ConfigureTopicAsync(string exchange, params (string Queue, string RoutingKey)[] bindings)
    {
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition { Name = exchange, Type = ExchangeType.Topic, Durable = true });
        foreach (var (queue, routingKey) in bindings)
        {
            topology.AddQueue(new QueueDefinition { Name = queue, Durable = true });
            topology.AddBinding(new BindingDefinition { SourceExchange = exchange, Destination = queue, RoutingKey = routingKey });
        }

        await Transport!.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);
    }

    private async Task<Task<TransportEnvelope>> ReceiveOneAsync(string queue)
    {
        var received = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Transport!.SubscribeAsync(TransportAddress.Queue(queue),
            async (message, context, cancellationToken) =>
            {
                await context.AcknowledgeAsync(cancellationToken);
                received.TrySetResult(message);
            }, cancellationToken: TestContext.Current.CancellationToken);
        return received.Task;
    }
}
