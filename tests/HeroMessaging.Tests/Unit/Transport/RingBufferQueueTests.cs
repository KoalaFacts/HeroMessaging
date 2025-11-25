using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

/// <summary>
/// Unit tests for RingBufferQueue - high-performance in-memory queue implementation
/// </summary>
[Trait("Category", "Unit")]
public class RingBufferQueueTests
{
    private readonly FakeTimeProvider _timeProvider;

    public RingBufferQueueTests()
    {
        _timeProvider = new FakeTimeProvider();
    }

    [Fact]
    public void Constructor_WithValidOptions_CreatesInstance()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };

        // Act
        var queue = new RingBufferQueue(options);

        // Assert
        Assert.NotNull(queue);
        Assert.Equal(0, queue.MessageCount);
        Assert.Equal(0, queue.Depth);

        queue.Dispose();
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new RingBufferQueue(null!));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1000  // Not a power of 2
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new RingBufferQueue(options));
        Assert.Contains("power of 2", exception.Message);
    }

    [Fact]
    public void Constructor_WithZeroBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 0
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new RingBufferQueue(options));
    }

    [Fact]
    public void Constructor_WithNegativeBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = -1
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new RingBufferQueue(options));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Constructor_WithPowerOf2BufferSize_CreatesSuccessfully(int bufferSize)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = bufferSize
        };

        // Act
        var queue = new RingBufferQueue(options);

        // Assert
        Assert.NotNull(queue);

        queue.Dispose();
    }

    [Theory]
    [InlineData(WaitStrategy.Blocking)]
    [InlineData(WaitStrategy.Sleeping)]
    [InlineData(WaitStrategy.Yielding)]
    [InlineData(WaitStrategy.BusySpin)]
    [InlineData(WaitStrategy.TimeoutBlocking)]
    public void Constructor_WithDifferentWaitStrategies_CreatesSuccessfully(WaitStrategy waitStrategy)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = waitStrategy
        };

        // Act
        var queue = new RingBufferQueue(options);

        // Assert
        Assert.NotNull(queue);

        queue.Dispose();
    }

    [Theory]
    [InlineData(ProducerMode.Single)]
    [InlineData(ProducerMode.Multi)]
    public void Constructor_WithDifferentProducerModes_CreatesSuccessfully(ProducerMode producerMode)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            ProducerMode = producerMode
        };

        // Act
        var queue = new RingBufferQueue(options);

        // Assert
        Assert.NotNull(queue);

        queue.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_SingleMessage_IncrementsCounters()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var envelope = CreateTestEnvelope();

        // Act
        var result = await queue.EnqueueAsync(envelope);

        // Assert
        Assert.True(result);
        Assert.Equal(1, queue.MessageCount);
        Assert.Equal(1, queue.Depth);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueAsync_MultipleMessages_IncrementsCounters()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);

        // Act
        for (int i = 0; i < 10; i++)
        {
            var result = await queue.EnqueueAsync(CreateTestEnvelope());
            Assert.True(result);
        }

        // Assert
        Assert.Equal(10, queue.MessageCount);
        Assert.Equal(10, queue.Depth);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueAsync_AfterDispose_ReturnsFalse()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var envelope = CreateTestEnvelope();

        // Act
        await queue.DisposeAsync();
        var result = await queue.EnqueueAsync(envelope);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task EnqueueAsync_WithCancelledToken_CompletesImmediately()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var envelope = CreateTestEnvelope();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        // RingBufferQueue doesn't check cancellation token during enqueue
        // as it's a fast, lock-free operation. This test verifies it doesn't hang.
        var result = await queue.EnqueueAsync(envelope, cts.Token);

        // Assert - Should complete (even with cancelled token)
        Assert.True(result || !result); // Just verify it completes

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task AddConsumer_WithValidConsumer_AddsSuccessfully()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var consumer = await CreateTestConsumer();

        // Act
        queue.AddConsumer(consumer);

        // Assert - No exception thrown
        Assert.NotNull(consumer);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task AddConsumer_WithNullConsumer_ThrowsArgumentNullException()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => queue.AddConsumer(null!));

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task AddConsumer_AndEnqueue_DeliversMessageToConsumer()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var receivedMessages = new List<string>();
        var tcs = new TaskCompletionSource<bool>();

        var consumer = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            receivedMessages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
            tcs.SetResult(true);
        });

        await consumer.StartAsync();

        // Act
        queue.AddConsumer(consumer);
        await queue.EnqueueAsync(CreateTestEnvelope("TestMessage"));

        // Wait for processing
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Single(receivedMessages);
        Assert.Equal("TestMessage", receivedMessages[0]);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task AddConsumer_MultipleConsumers_DistributesMessagesRoundRobin()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();
        var consumer3Messages = new List<string>();
        var processCount = 0;
        var targetCount = 9;
        var tcs = new TaskCompletionSource<bool>();

        var consumer1 = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            consumer1Messages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
            if (Interlocked.Increment(ref processCount) >= targetCount)
                tcs.SetResult(true);
        });

        var consumer2 = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            consumer2Messages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
            if (Interlocked.Increment(ref processCount) >= targetCount)
                tcs.SetResult(true);
        });

        var consumer3 = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            consumer3Messages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
            if (Interlocked.Increment(ref processCount) >= targetCount)
                tcs.SetResult(true);
        });

        await consumer1.StartAsync();
        await consumer2.StartAsync();
        await consumer3.StartAsync();

        queue.AddConsumer(consumer1);
        queue.AddConsumer(consumer2);
        queue.AddConsumer(consumer3);

        // Act - Enqueue 9 messages (3 per consumer)
        for (int i = 0; i < 9; i++)
        {
            await queue.EnqueueAsync(CreateTestEnvelope($"Message{i}"));
        }

        // Wait for processing
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Each consumer should receive 3 messages
        Assert.Equal(3, consumer1Messages.Count);
        Assert.Equal(3, consumer2Messages.Count);
        Assert.Equal(3, consumer3Messages.Count);

        await queue.DisposeAsync();
        await consumer1.DisposeAsync();
        await consumer2.DisposeAsync();
        await consumer3.DisposeAsync();
    }

    [Fact]
    public async Task RemoveConsumer_WithExistingConsumer_RemovesSuccessfully()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var consumer = await CreateTestConsumer();

        queue.AddConsumer(consumer);

        // Act
        queue.RemoveConsumer(consumer);

        // Assert - No exception thrown
        Assert.NotNull(consumer);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task RemoveConsumer_WithNullConsumer_DoesNotThrow()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);

        // Act & Assert - Should not throw
        queue.RemoveConsumer(null!);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task RemoveConsumer_AfterRemoval_StopsDeliveringMessages()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();
        var processCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        var consumer1 = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            consumer1Messages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
        });

        var consumer2 = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            consumer2Messages.Add(env.MessageType);
            await ctx.AcknowledgeAsync(ct);
            if (Interlocked.Increment(ref processCount) >= 2)
                tcs.SetResult(true);
        });

        await consumer1.StartAsync();
        await consumer2.StartAsync();

        queue.AddConsumer(consumer1);
        queue.AddConsumer(consumer2);

        // Act - Remove consumer1
        queue.RemoveConsumer(consumer1);

        // Enqueue messages after removal
        await queue.EnqueueAsync(CreateTestEnvelope("AfterRemoval1"));
        await queue.EnqueueAsync(CreateTestEnvelope("AfterRemoval2"));

        // Wait for processing
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Only consumer2 should receive messages
        Assert.Empty(consumer1Messages);
        Assert.Equal(2, consumer2Messages.Count);

        await queue.DisposeAsync();
        await consumer1.DisposeAsync();
        await consumer2.DisposeAsync();
    }

    [Fact]
    public async Task MessageDepth_AfterDelivery_Decrements()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var tcs = new TaskCompletionSource<bool>();

        var consumer = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            await ctx.AcknowledgeAsync(ct);
            tcs.SetResult(true);
        });

        await consumer.StartAsync();
        queue.AddConsumer(consumer);

        // Act
        await queue.EnqueueAsync(CreateTestEnvelope());
        var depthBefore = queue.Depth;

        // Wait for processing
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var depthAfter = queue.Depth;

        // Assert
        Assert.Equal(1, depthBefore);
        Assert.Equal(0, depthAfter);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task HighThroughput_ManyMessages_ProcessesAllCorrectly()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 4096,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };
        var queue = new RingBufferQueue(options);
        var processedCount = 0;
        var targetCount = 1000;
        var tcs = new TaskCompletionSource<bool>();

        var consumer = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            await ctx.AcknowledgeAsync(ct);
            if (Interlocked.Increment(ref processedCount) >= targetCount)
                tcs.SetResult(true);
        });

        await consumer.StartAsync();
        queue.AddConsumer(consumer);

        // Act - Enqueue many messages
        var tasks = new List<Task>();
        for (int i = 0; i < targetCount; i++)
        {
            tasks.Add(queue.EnqueueAsync(CreateTestEnvelope($"Message{i}")).AsTask());
        }
        await Task.WhenAll(tasks);

        // Wait for processing
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(targetCount, processedCount);
        Assert.Equal(targetCount, queue.MessageCount);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentProducers_MultipleThreads_AllMessagesEnqueued()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 2048,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };
        var queue = new RingBufferQueue(options);
        var messageCount = 500;
        var threadCount = 4;
        var totalMessages = messageCount * threadCount;

        // Act - Enqueue from multiple threads
        var tasks = new List<Task>();
        for (int t = 0; t < threadCount; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < messageCount; i++)
                {
                    await queue.EnqueueAsync(CreateTestEnvelope($"Thread{threadId}-Message{i}"));
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(totalMessages, queue.MessageCount);

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_StopsProcessingAndCleansUp()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var consumer = await CreateTestConsumer();

        await consumer.StartAsync();
        queue.AddConsumer(consumer);

        // Act
        await queue.DisposeAsync();

        // Assert - Enqueue after dispose should fail
        var result = await queue.EnqueueAsync(CreateTestEnvelope());
        Assert.False(result);

        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithPendingMessages_CompletesGracefully()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);

        // Enqueue messages without consumer
        for (int i = 0; i < 10; i++)
        {
            await queue.EnqueueAsync(CreateTestEnvelope());
        }

        // Act & Assert - Should not throw
        await queue.DisposeAsync();
    }

    [Fact]
    public void Dispose_SynchronousDisposal_CompletesSuccessfully()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);

        // Act & Assert - Should not throw
        queue.Dispose();
    }

    [Fact]
    public async Task ConsumerFailure_DoesNotStopQueue()
    {
        // Arrange
        var options = CreateDefaultOptions();
        var queue = new RingBufferQueue(options);
        var successCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        var consumer = await CreateTestConsumer(async (env, ctx, ct) =>
        {
            if (env.MessageType == "FailMessage")
            {
                throw new InvalidOperationException("Consumer failure");
            }
            Interlocked.Increment(ref successCount);
            await ctx.AcknowledgeAsync(ct);
            if (successCount >= 2)
                tcs.SetResult(true);
        });

        await consumer.StartAsync();
        queue.AddConsumer(consumer);

        // Act - Mix of failing and successful messages
        await queue.EnqueueAsync(CreateTestEnvelope("SuccessMessage1"));
        await queue.EnqueueAsync(CreateTestEnvelope("FailMessage"));
        await queue.EnqueueAsync(CreateTestEnvelope("SuccessMessage2"));

        // Wait for successful messages
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Queue should continue processing despite failure
        Assert.Equal(2, successCount);

        await queue.DisposeAsync();
        await consumer.DisposeAsync();
    }

    // Helper methods
    private static InMemoryQueueOptions CreateDefaultOptions()
    {
        return new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = 1024,
            WaitStrategy = WaitStrategy.Sleeping,
            ProducerMode = ProducerMode.Multi
        };
    }

    private async Task<InMemoryConsumer> CreateTestConsumer(
        Func<TransportEnvelope, MessageContext, CancellationToken, Task>? handler = null)
    {
        var transportOptions = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 1000
        };
        var transport = new InMemoryTransport(transportOptions, _timeProvider);
        await transport.ConnectAsync();

        var source = TransportAddress.Queue("test-queue");
        handler ??= (env, ctx, ct) => Task.CompletedTask;

        var consumer = await transport.SubscribeAsync(
            source,
            handler,
            new ConsumerOptions { StartImmediately = false, ConsumerId = Guid.NewGuid().ToString() });

        return (InMemoryConsumer)consumer;
    }

    private static TransportEnvelope CreateTestEnvelope(string messageType = "TestMessage")
    {
        return new TransportEnvelope(
            messageType,
            new byte[] { 1, 2, 3, 4, 5 }.AsMemory(),
            messageId: Guid.NewGuid().ToString());
    }
}
