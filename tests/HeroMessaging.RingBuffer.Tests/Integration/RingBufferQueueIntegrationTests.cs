using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Integration;

[Trait("Category", "Integration")]
public class RingBufferQueueIntegrationTests
{
    private class TestConsumer
    {
        private readonly List<TransportEnvelope> _receivedMessages = new();
        private readonly object _lock = new();

        public IReadOnlyList<TransportEnvelope> ReceivedMessages
        {
            get
            {
                lock (_lock)
                {
                    return _receivedMessages.ToList();
                }
            }
        }

        public InMemoryConsumer CreateConsumer(string consumerId = "test-consumer")
        {
            var transport = new InMemoryTransport("test-transport", TimeProvider.System);
            var source = new TransportAddress("test-queue", TransportAddressType.Queue);
            var options = new ConsumerOptions { AutoAcknowledge = true };

            return new InMemoryConsumer(
                consumerId,
                source,
                async (envelope, context, ct) =>
                {
                    lock (_lock)
                    {
                        _receivedMessages.Add(envelope);
                    }
                    await Task.CompletedTask;
                },
                options,
                transport,
                TimeProvider.System);
        }
    }

    [Fact]
    public async Task RingBufferQueue_EnqueueAndConsume_SingleMessage_Success()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        queue.AddConsumer(consumer);

        var envelope = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1, 2, 3 },
            messageId: Guid.NewGuid().ToString());

        // Act
        var result = await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);

        // Wait for processing
        await Task.Delay(200);

        // Assert
        Assert.True(result);
        Assert.Single(testConsumer.ReceivedMessages);
        Assert.Equal(envelope.MessageId, testConsumer.ReceivedMessages[0].MessageId);

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RingBufferQueue_EnqueueMultiple_OrderPreserved()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        queue.AddConsumer(consumer);

        var messageIds = new List<string>();

        // Act - Enqueue 10 messages
        for (int i = 0; i < 10; i++)
        {
            var messageId = $"msg-{i}";
            messageIds.Add(messageId);

            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: messageId);

            await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
        }

        // Wait for all messages to be processed
        await Task.Delay(500);

        // Assert
        Assert.Equal(10, testConsumer.ReceivedMessages.Count);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(messageIds[i], testConsumer.ReceivedMessages[i].MessageId);
        }

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RingBufferQueue_MultipleConsumers_RoundRobinDistribution()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 32,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };

        var queue = new RingBufferQueue(options);

        var consumer1 = new TestConsumer();
        var consumer2 = new TestConsumer();
        var consumer3 = new TestConsumer();

        var inmemConsumer1 = consumer1.CreateConsumer("consumer-1");
        var inmemConsumer2 = consumer2.CreateConsumer("consumer-2");
        var inmemConsumer3 = consumer3.CreateConsumer("consumer-3");

        await inmemConsumer1.StartAsync(TestContext.Current.CancellationToken);
        await inmemConsumer2.StartAsync(TestContext.Current.CancellationToken);
        await inmemConsumer3.StartAsync(TestContext.Current.CancellationToken);

        queue.AddConsumer(inmemConsumer1);
        queue.AddConsumer(inmemConsumer2);
        queue.AddConsumer(inmemConsumer3);

        // Act - Enqueue 30 messages
        for (int i = 0; i < 30; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}");

            await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
        }

        // Wait for processing
        await Task.Delay(1000);

        // Assert - Each consumer should have received ~10 messages
        var total = consumer1.ReceivedMessages.Count +
                   consumer2.ReceivedMessages.Count +
                   consumer3.ReceivedMessages.Count;

        Assert.Equal(30, total);

        // Each consumer should have received some messages (round-robin)
        Assert.True(consumer1.ReceivedMessages.Count > 0);
        Assert.True(consumer2.ReceivedMessages.Count > 0);
        Assert.True(consumer3.ReceivedMessages.Count > 0);

        // Cleanup
        await inmemConsumer1.StopAsync(TestContext.Current.CancellationToken);
        await inmemConsumer2.StopAsync(TestContext.Current.CancellationToken);
        await inmemConsumer3.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RingBufferQueue_HighThroughput_1000Messages()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 256,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        queue.AddConsumer(consumer);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act - Enqueue 1000 messages
        for (int i = 0; i < 1000; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}");

            await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
        }

        sw.Stop();

        // Wait for all to be processed
        await Task.Delay(2000);

        // Assert
        Assert.Equal(1000, testConsumer.ReceivedMessages.Count);

        // Verify throughput (should be very fast)
        var throughput = 1000.0 / sw.Elapsed.TotalSeconds;
        Assert.True(throughput > 10000, $"Throughput: {throughput:N0} msg/s (expected >10K msg/s)");

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void RingBufferQueue_InvalidBufferSize_ThrowsArgumentException()
    {
        // Arrange - Non-power-of-2 buffer size
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 100, // Not power of 2
            WaitStrategy = WaitStrategy.Sleeping
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new RingBufferQueue(options));
        Assert.Contains("power of 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RingBufferQueue_Metrics_TrackCorrectly()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);

        // Act
        Assert.Equal(0, queue.MessageCount);
        Assert.Equal(0, queue.Depth);

        for (int i = 0; i < 5; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: new byte[] { (byte)i },
                messageId: $"msg-{i}");

            await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Equal(5, queue.MessageCount);

        // Cleanup
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(WaitStrategy.Sleeping)]
    [InlineData(WaitStrategy.Yielding)]
    [InlineData(WaitStrategy.Blocking)]
    public async Task RingBufferQueue_DifferentWaitStrategies_AllWork(WaitStrategy strategy)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = strategy,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        queue.AddConsumer(consumer);

        // Act
        for (int i = 0; i < 5; i++)
        {
            var envelope = new TransportEnvelope(
                messageType: "TestMessage",
                body: BitConverter.GetBytes(i),
                messageId: $"msg-{i}");

            await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
        }

        await Task.Delay(200);

        // Assert
        Assert.Equal(5, testConsumer.ReceivedMessages.Count);

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RingBufferQueue_AddRemoveConsumer_WorksCorrectly()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 16,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Single
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Act - Add consumer
        queue.AddConsumer(consumer);

        var envelope1 = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 1 },
            messageId: "msg-1");

        await queue.EnqueueAsync(envelope1, TestContext.Current.CancellationToken);
        await Task.Delay(100);

        Assert.Single(testConsumer.ReceivedMessages);

        // Remove consumer
        queue.RemoveConsumer(consumer);

        var envelope2 = new TransportEnvelope(
            messageType: "TestMessage",
            body: new byte[] { 2 },
            messageId: "msg-2");

        await queue.EnqueueAsync(envelope2, TestContext.Current.CancellationToken);
        await Task.Delay(100);

        // Assert - Should still be 1 (consumer was removed)
        Assert.Single(testConsumer.ReceivedMessages);

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RingBufferQueue_ConcurrentEnqueue_ThreadSafe()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 512,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi // Multi-producer for thread safety
        };

        var queue = new RingBufferQueue(options);
        var testConsumer = new TestConsumer();
        var consumer = testConsumer.CreateConsumer();

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        queue.AddConsumer(consumer);

        const int threadCount = 10;
        const int messagesPerThread = 10;

        // Act - Enqueue from multiple threads
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            tasks[t] = Task.Run(async () =>
            {
                for (int i = 0; i < messagesPerThread; i++)
                {
                    var envelope = new TransportEnvelope(
                        messageType: "TestMessage",
                        body: BitConverter.GetBytes(i),
                        messageId: $"thread-{threadId}-msg-{i}");

                    await queue.EnqueueAsync(envelope, TestContext.Current.CancellationToken);
                }
            });
        }

        await Task.WhenAll(tasks);
        await Task.Delay(1000); // Wait for processing

        // Assert
        Assert.Equal(threadCount * messagesPerThread, testConsumer.ReceivedMessages.Count);

        // Verify all messages are unique
        var uniqueIds = testConsumer.ReceivedMessages.Select(m => m.MessageId).Distinct().Count();
        Assert.Equal(threadCount * messagesPerThread, uniqueIds);

        // Cleanup
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        await queue.DisposeAsync(TestContext.Current.CancellationToken);
    }
}
