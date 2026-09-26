using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public class RabbitMqShutdownIntegrationTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task DisconnectAsync_StartsStoppingAllConsumersBeforeWaiting()
    {
        var firstQueue = CreateQueueName();
        var secondQueue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = firstQueue, Durable = true });
        topology.AddQueue(new QueueDefinition { Name = secondQueue, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var first = await Transport.SubscribeAsync(
            TransportAddress.Queue(firstQueue),
            async (_, _, _) =>
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            },
            new ConsumerOptions { AutoAcknowledge = true }, timeout.Token);
        var second = await Transport.SubscribeAsync(
            TransportAddress.Queue(secondQueue),
            async (_, _, _) =>
            {
                secondStarted.SetResult();
                await releaseSecond.Task;
            },
            new ConsumerOptions { AutoAcknowledge = true }, timeout.Token);

        try
        {
            await Transport.SendAsync(TransportAddress.Queue(firstQueue), CreateTestEnvelope(), cancellationToken: timeout.Token);
            await Transport.SendAsync(TransportAddress.Queue(secondQueue), CreateTestEnvelope(), cancellationToken: timeout.Token);
            await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(timeout.Token);

            using var cancelledWait = new CancellationTokenSource();
            var disconnect = Transport.DisconnectAsync(cancelledWait.Token);
            Assert.False(first.IsActive);
            Assert.False(second.IsActive);
            cancelledWait.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnect);

            var drain = Transport.DisconnectAsync(timeout.Token);
            Assert.False(drain.IsCompleted);
            releaseFirst.SetResult();
            releaseSecond.SetResult();
            await drain.WaitAsync(timeout.Token);
            Assert.Equal(TransportState.Disconnected, Transport.State);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
        }
    }

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
