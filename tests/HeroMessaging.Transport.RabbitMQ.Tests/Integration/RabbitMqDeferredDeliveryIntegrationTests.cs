using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class RabbitMqDeferredDeliveryIntegrationTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task StopAsync_DuringDefer_RequeuesWithoutWaitingForDelay()
    {
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = await Transport.SubscribeAsync(
            TransportAddress.Queue(queueName),
            async (_, context, ct) =>
            {
                deferred.TrySetResult();
                await context.DeferAsync(TimeSpan.FromSeconds(8), ct);
            },
            new ConsumerOptions(), timeout.Token);

        try
        {
            await Transport.SendAsync(TransportAddress.Queue(queueName), CreateTestEnvelope(), cancellationToken: timeout.Token);
            await deferred.Task.WaitAsync(timeout.Token);

            await consumer.StopAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
            Assert.Equal(1, consumer.GetMetrics().MessagesProcessed);
        }
        finally
        {
            await consumer.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeferAsync_RedeliversAfterRequestedDelay()
    {
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var redelivered = new TaskCompletionSource<(TimeSpan Elapsed, bool WasRedelivered)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveries = 0;
        var started = 0L;

        var consumer = await Transport.SubscribeAsync(
            TransportAddress.Queue(queueName),
            async (_, context, ct) =>
            {
                if (Interlocked.Increment(ref deliveries) == 1)
                {
                    started = Stopwatch.GetTimestamp();
                    await context.DeferAsync(TimeSpan.FromMilliseconds(300), ct);
                    return;
                }

                await context.AcknowledgeAsync(ct);
                redelivered.TrySetResult((
                    Stopwatch.GetElapsedTime(started),
                    Assert.IsType<bool>(context.Properties["Redelivered"])));
            },
            new ConsumerOptions(), timeout.Token);

        try
        {
            await Transport.SendAsync(TransportAddress.Queue(queueName), CreateTestEnvelope(), cancellationToken: timeout.Token);
            var (elapsed, wasRedelivered) = await redelivered.Task.WaitAsync(timeout.Token);

            Assert.True(elapsed >= TimeSpan.FromMilliseconds(250));
            Assert.True(wasRedelivered);
            Assert.Equal(2, Volatile.Read(ref deliveries));
        }
        finally
        {
            await consumer.DisposeAsync();
        }
    }
}
