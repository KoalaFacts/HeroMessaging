using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public class RabbitMqShutdownIntegrationTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task StopAsync_WaitsForInFlightHandlerAgainstBroker()
    {
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var consumer = await Transport.SubscribeAsync(
            TransportAddress.Queue(queueName),
            async (_, _, _) =>
            {
                handlerStarted.SetResult();
                await releaseHandler.Task;
            },
            new ConsumerOptions { AutoAcknowledge = true }, timeout.Token);

        try
        {
            await Transport.SendAsync(TransportAddress.Queue(queueName), CreateTestEnvelope(), cancellationToken: timeout.Token);
            await handlerStarted.Task.WaitAsync(timeout.Token);

            var stop = consumer.StopAsync(timeout.Token);
            Assert.False(stop.IsCompleted);

            releaseHandler.SetResult();
            await stop.WaitAsync(timeout.Token);
            Assert.Equal(1, consumer.GetMetrics().MessagesProcessed);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await consumer.DisposeAsync();
        }
    }
}
