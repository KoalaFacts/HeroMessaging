using System.Collections.Concurrent;
using System.Reflection;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ.Connection;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Verifies that a consumer's long-lived channel does not permanently consume
/// publisher channel-pool capacity after the consumer has fully shut down.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqConsumerChannelCapacityIntegrationTests : RabbitMqIntegrationTestBase
{
    [Fact]
    public async Task DisposedConsumer_RetiresOwnedChannelAndRestoresPublisherCapacity()
    {
        var token = TestContext.Current.CancellationToken;
        Options!.MaxChannelsPerConnection = 1;

        var queueName = CreateQueueName();
        var address = new TransportAddress(queueName, TransportAddressType.Queue);
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, token);

        // Channel-pool statistics are internal; the transport has no public
        // per-connection capacity metric. Inspect exactly that invariant rather
        // than treating successful temporary-channel fallback as correct reuse.
        var poolsField = typeof(RabbitMqTransport).GetField(
            "_channelPools", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(poolsField);
        var pools = Assert.IsType<ConcurrentDictionary<string, RabbitMqChannelPool>>(
            poolsField.GetValue(Transport));
        var pool = Assert.Single(pools.Values);
        Assert.Equal((1, 1), pool.GetStatistics());

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = await Transport.SubscribeAsync(
            address,
            (_, _, _) =>
            {
                handled.TrySetResult();
                return Task.CompletedTask;
            },
            new ConsumerOptions { ConsumerId = Guid.NewGuid().ToString("N") },
            token);

        Assert.Equal((1, 0), pool.GetStatistics());
        await Transport.SendConfirmedAsync(address, CreateTestEnvelope(), token);
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        // Dispose drains the consumer's in-flight delivery and ACK before
        // closing its borrowed channel. The retained lease must be retired.
        await consumer.DisposeAsync();
        Assert.Equal(0, (await Transport.GetHealthAsync(token)).ActiveConsumers);
        Assert.Equal((0, 0), pool.GetStatistics());

        // The next confirmed send should establish a reusable retained channel,
        // not allocate and destroy another overflow channel.
        await Transport.SendConfirmedAsync(address, CreateTestEnvelope(), token);
        Assert.Equal((1, 1), pool.GetStatistics());
    }

    [Fact]
    public async Task BrokerClosedBorrowedChannel_RetiresCapacityBeforeOwnerReleasesIt()
    {
        var token = TestContext.Current.CancellationToken;
        // Automatic recovery wraps the underlying broker channel; its shutdown
        // event sender need not be the IChannel returned by CreateChannelAsync.
        Assert.True(Options!.AutoReconnect);
        Options.MaxChannelsPerConnection = 1;

        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = CreateQueueName(), Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, token);

        var poolsField = typeof(RabbitMqTransport).GetField(
            "_channelPools", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(poolsField);
        var pools = Assert.IsType<ConcurrentDictionary<string, RabbitMqChannelPool>>(
            poolsField.GetValue(Transport));
        var pool = Assert.Single(pools.Values);
        Assert.Equal((1, 1), pool.GetStatistics());

        // Model an active consumer-owned lease: it is not returned to the bag.
        // A broker channel shutdown must release counted capacity even when the
        // consumer has not yet called its explicit retirement callback.
        var borrowed = await pool.AcquireChannelAsync(token);
        Assert.Equal((1, 0), pool.GetStatistics());
        try
        {
            await borrowed.CloseAsync(cancellationToken: token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (pool.GetStatistics().Total != 0 && !deadline.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);

            Assert.Equal((0, 0), pool.GetStatistics());
        }
        finally
        {
            // Cleanup must not influence the assertion above.
            pool.ReleaseChannel(borrowed);
        }
    }

}
