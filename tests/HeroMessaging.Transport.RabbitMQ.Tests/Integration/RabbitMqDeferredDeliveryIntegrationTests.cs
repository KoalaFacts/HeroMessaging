using System.Diagnostics;
using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class RabbitMqDeferredDeliveryIntegrationTests : RabbitMqIntegrationTestBase
{
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
