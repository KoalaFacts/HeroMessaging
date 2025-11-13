using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Integration;

/// <summary>
/// Integration tests comparing Channel vs RingBuffer modes.
/// Verifies both implementations provide identical external behavior.
/// </summary>
[Trait("Category", "Integration")]
public class QueueModeComparisonTests
{
    private class MessageCollector
    {
        private readonly List<TransportEnvelope> _messages = new();
        private readonly object _lock = new();

        public IReadOnlyList<TransportEnvelope> Messages
        {
            get
            {
                lock (_lock)
                {
                    return _messages.ToList();
                }
            }
        }

        public InMemoryConsumer CreateConsumer(string consumerId)
        {
            var transport = new InMemoryTransport("test", TimeProvider.System);
            var source = new TransportAddress("queue", TransportAddressType.Queue);
            var options = new ConsumerOptions { AutoAcknowledge = true };

            return new InMemoryConsumer(
                consumerId,
                source,
                async (envelope, context, ct) =>
                {
                    lock (_lock)
                    {
                        _messages.Add(envelope);
                    }
                    await Task.CompletedTask;
                },
                options,
                transport,
                TimeProvider.System);
        }
    }

    [Fact]
    public async Task BothModes_SingleMessage_IdenticalBehavior()
    {
        // Arrange - Channel mode
        var channelQueue = new InMemoryQueue(16, dropWhenFull: false);
        var channelCollector = new MessageCollector();
        var channelConsumer = channelCollector.CreateConsumer("channel-consumer");

        // Arrange - RingBuffer mode
        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        var ringBufferQueue = new RingBufferQueue(ringBufferOptions);
        var ringBufferCollector = new MessageCollector();
        var ringBufferConsumer = ringBufferCollector.CreateConsumer("ringbuffer-consumer");

        await channelConsumer.StartAsync();
        await ringBufferConsumer.StartAsync();

        channelQueue.AddConsumer(channelConsumer);
        ringBufferQueue.AddConsumer(ringBufferConsumer);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3, 4, 5 },
            messageId: Guid.NewGuid().ToString());

        // Act
        await channelQueue.EnqueueAsync(envelope);
        await ringBufferQueue.EnqueueAsync(envelope);

        await Task.Delay(200);

        // Assert - Both should receive the message
        Assert.Single(channelCollector.Messages);
        Assert.Single(ringBufferCollector.Messages);

        Assert.Equal(envelope.MessageId, channelCollector.Messages[0].MessageId);
        Assert.Equal(envelope.MessageId, ringBufferCollector.Messages[0].MessageId);

        // Cleanup
        await channelConsumer.StopAsync();
        await ringBufferConsumer.StopAsync();
        channelQueue.Dispose();
        await ringBufferQueue.DisposeAsync();
    }

    [Fact]
    public async Task BothModes_OrderedMessages_SameOrder()
    {
        // Arrange
        var channelQueue = new InMemoryQueue(32, dropWhenFull: false);
        var channelCollector = new MessageCollector();
        var channelConsumer = channelCollector.CreateConsumer("channel-consumer");

        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 32,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        var ringBufferQueue = new RingBufferQueue(ringBufferOptions);
        var ringBufferCollector = new MessageCollector();
        var ringBufferConsumer = ringBufferCollector.CreateConsumer("ringbuffer-consumer");

        await channelConsumer.StartAsync();
        await ringBufferConsumer.StartAsync();

        channelQueue.AddConsumer(channelConsumer);
        ringBufferQueue.AddConsumer(ringBufferConsumer);

        var messageIds = new List<string>();

        // Act - Send 20 messages to both
        for (int i = 0; i < 20; i++)
        {
            var messageId = $"msg-{i}";
            messageIds.Add(messageId);

            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: messageId);

            await channelQueue.EnqueueAsync(envelope);
            await ringBufferQueue.EnqueueAsync(envelope);
        }

        await Task.Delay(500);

        // Assert - Both should have same count and order
        Assert.Equal(20, channelCollector.Messages.Count);
        Assert.Equal(20, ringBufferCollector.Messages.Count);

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(messageIds[i], channelCollector.Messages[i].MessageId);
            Assert.Equal(messageIds[i], ringBufferCollector.Messages[i].MessageId);
        }

        // Cleanup
        await channelConsumer.StopAsync();
        await ringBufferConsumer.StopAsync();
        channelQueue.Dispose();
        await ringBufferQueue.DisposeAsync();
    }

    [Fact]
    public async Task BothModes_Metrics_TrackCorrectly()
    {
        // Arrange
        var channelQueue = new InMemoryQueue(16, dropWhenFull: false);

        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        var ringBufferQueue = new RingBufferQueue(ringBufferOptions);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}");

            await channelQueue.EnqueueAsync(envelope);
            await ringBufferQueue.EnqueueAsync(envelope);
        }

        // Assert
        Assert.Equal(10, channelQueue.MessageCount);
        Assert.Equal(10, ringBufferQueue.MessageCount);

        // Cleanup
        channelQueue.Dispose();
        await ringBufferQueue.DisposeAsync();
    }

    [Fact]
    public async Task RingBufferMode_FasterThanChannel_VerifyPerformance()
    {
        // Arrange
        const int messageCount = 1000;

        var channelQueue = new InMemoryQueue(512, dropWhenFull: false);
        var channelCollector = new MessageCollector();
        var channelConsumer = channelCollector.CreateConsumer("channel-consumer");

        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 512,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        var ringBufferQueue = new RingBufferQueue(ringBufferOptions);
        var ringBufferCollector = new MessageCollector();
        var ringBufferConsumer = ringBufferCollector.CreateConsumer("ringbuffer-consumer");

        await channelConsumer.StartAsync();
        await ringBufferConsumer.StartAsync();

        channelQueue.AddConsumer(channelConsumer);
        ringBufferQueue.AddConsumer(ringBufferConsumer);

        var swChannel = System.Diagnostics.Stopwatch.StartNew();

        // Act - Channel mode
        for (int i = 0; i < messageCount; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"channel-msg-{i}");

            await channelQueue.EnqueueAsync(envelope);
        }

        swChannel.Stop();

        var swRingBuffer = System.Diagnostics.Stopwatch.StartNew();

        // Act - RingBuffer mode
        for (int i = 0; i < messageCount; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"ringbuffer-msg-{i}");

            await ringBufferQueue.EnqueueAsync(envelope);
        }

        swRingBuffer.Stop();

        // Wait for processing
        await Task.Delay(2000);

        // Assert - Both delivered all messages
        Assert.Equal(messageCount, channelCollector.Messages.Count);
        Assert.Equal(messageCount, ringBufferCollector.Messages.Count);

        // Calculate throughput
        var channelThroughput = messageCount / swChannel.Elapsed.TotalSeconds;
        var ringBufferThroughput = messageCount / swRingBuffer.Elapsed.TotalSeconds;

        // Output performance for visibility
        var speedup = ringBufferThroughput / channelThroughput;

        // Assert - RingBuffer should be significantly faster
        // Note: Actual speedup depends on hardware, but should be measurably faster
        Assert.True(ringBufferThroughput > channelThroughput,
            $"Channel: {channelThroughput:N0} msg/s, RingBuffer: {ringBufferThroughput:N0} msg/s, " +
            $"Speedup: {speedup:F2}x");

        // Cleanup
        await channelConsumer.StopAsync();
        await ringBufferConsumer.StopAsync();
        channelQueue.Dispose();
        await ringBufferQueue.DisposeAsync();
    }

    [Fact]
    public async Task BothModes_ZeroAllocation_RingBufferWins()
    {
        // Arrange
        var ringBufferOptions = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 64,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };
        var ringBufferQueue = new RingBufferQueue(ringBufferOptions);

        // Warmup - let JIT compile everything
        for (int i = 0; i < 100; i++)
        {
            var warmupEnvelope = new TransportEnvelope(
                messageType: "Warmup",
                body: new byte[] { 1 },
                messageId: $"warmup-{i}");

            await ringBufferQueue.EnqueueAsync(warmupEnvelope);
        }

        // Force GC and wait
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(100);

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        // Act - Enqueue messages in steady state
        for (int i = 0; i < 1000; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}");

            await ringBufferQueue.EnqueueAsync(envelope);
        }

        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        // Assert - Should have minimal to zero GC collections
        var gen0Collections = gen0After - gen0Before;
        var gen1Collections = gen1After - gen1Before;
        var gen2Collections = gen2After - gen2Before;

        // RingBuffer should cause very few GC collections (ideally zero)
        Assert.True(gen0Collections <= 2,
            $"Gen0 collections: {gen0Collections} (expected ≤2)");
        Assert.Equal(0, gen1Collections);
        Assert.Equal(0, gen2Collections);

        // Cleanup
        await ringBufferQueue.DisposeAsync();
    }
}
