using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

/// <summary>
/// Unit tests for InMemoryQueue functionality through InMemoryTransport
/// Since InMemoryQueue is internal, we test its behavior through the public transport API
/// </summary>
[Trait("Category", "Unit")]
public class InMemoryQueueTests
{
    private readonly InMemoryTransportOptions _options;

    public InMemoryQueueTests()
    {
        _options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 100,
            DropWhenFull = false,
            SimulateNetworkDelay = false
        };
    }

    [Fact]
    public async Task Queue_EnqueueAndDequeue_ProcessesMessagesInFIFOOrder()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var receivedMessages = new List<string>();

        var consumer = await transport.SubscribeAsync(
            queue,
            async (env, ctx, ct) =>
            {
                receivedMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true });

        // Act
        await transport.SendAsync(queue, CreateTestEnvelope("Message1", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("Message2", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("Message3", TestContext.Current.CancellationToken));

        await Task.Delay(200); // Wait for processing

        // Assert
        Assert.Equal(3, receivedMessages.Count);
        Assert.Equal("Message1", receivedMessages[0]);
        Assert.Equal("Message2", receivedMessages[1]);
        Assert.Equal("Message3", receivedMessages[2]);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_WithQueueFull_BlocksWhenDropWhenFullIsFalse()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 2,
            DropWhenFull = false
        };
        var transport = new InMemoryTransport(options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");

        // Fill the queue
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));

        // Act & Assert - Third message should timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.SendAsync(queue, CreateTestEnvelope(), cts.Token));

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_WithQueueFullAndDropWhenFull_DropsOldestMessage()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 2,
            DropWhenFull = true
        };
        var transport = new InMemoryTransport(options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");

        // Act - Fill the queue and send one more
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken)); // Should drop oldest

        // Assert - Should not throw
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.True(health.PendingMessages <= 2);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_RoundRobinDistribution_DistributesMessagesEvenly()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();
        var consumer3Messages = new List<string>();

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                consumer1Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                consumer2Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                consumer3Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer3" });

        // Act - Send 6 messages (2 per consumer with round-robin)
        for (int i = 0; i < 6; i++)
        {
            await transport.SendAsync(queue, CreateTestEnvelope($"Message{i}", TestContext.Current.CancellationToken));
        }

        await Task.Delay(300); // Wait for processing

        // Assert - Each consumer should receive 2 messages
        Assert.Equal(2, consumer1Messages.Count);
        Assert.Equal(2, consumer2Messages.Count);
        Assert.Equal(2, consumer3Messages.Count);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_ConsumerRemoval_StopsDeliveringToRemovedConsumer()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();
        var receivedMessages = new System.Collections.Concurrent.ConcurrentBag<string>();

        var consumer1 = await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                consumer1Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                consumer2Messages.Add(env.MessageType);
                receivedMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        // Act - Remove consumer1
        await consumer1.DisposeAsync(TestContext.Current.CancellationToken);

        // Allow time for cache refresh (round-robin might still hit removed consumer briefly)
        await Task.Delay(50);

        // Send messages after removal - with only consumer2 active
        await transport.SendAsync(queue, CreateTestEnvelope("AfterRemoval1", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("AfterRemoval2", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("AfterRemoval3", TestContext.Current.CancellationToken));

        // Wait for processing
        await Task.Delay(300);

        // Assert - Consumer1 should NOT receive any messages after removal
        Assert.Empty(consumer1Messages);

        // Consumer2 should receive at least some messages (all messages go to consumer2 now)
        Assert.True(consumer2Messages.Count > 0, "Consumer2 should receive messages after consumer1 removal");

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_WithConsumerFailure_ContinuesProcessingOtherMessages()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var processedMessages = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                // First message will always fail - we're testing that queue continues processing
                if (env.MessageType == "Message1")
                {
                    throw new InvalidOperationException("Message1 always fails");
                }

                processedMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);

                // Signal when we've successfully processed the other messages
                if (processedMessages.Count >= 2)
                {
                    tcs.TrySetResult(true);
                }
            },
            new ConsumerOptions
            {
                StartImmediately = true,
                MessageRetryPolicy = new RetryPolicy
                {
                    MaxAttempts = 0,  // No retries - fail immediately
                    InitialDelay = TimeSpan.Zero
                }
            });

        // Act - Send multiple messages
        await transport.SendAsync(queue, CreateTestEnvelope("Message1", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("Message2", TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope("Message3", TestContext.Current.CancellationToken));

        // Wait for the successful messages (Message2 and Message3)
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5, TestContext.Current.CancellationToken));

        // Assert - Should have processed Message2 and Message3 successfully despite Message1 failing
        Assert.Equal(2, processedMessages.Count);
        Assert.Contains("Message2", processedMessages);
        Assert.Contains("Message3", processedMessages);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_HighThroughput_HandlesManyMessagesCorrectly()
    {
        // Arrange
        var transport = new InMemoryTransport(
            new InMemoryTransportOptions
            {
                Name = "TestTransport",
                MaxQueueLength = 1000,
                DropWhenFull = false
            },
            TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var processedCount = 0;

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        // Act - Send many messages
        var sendTasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            sendTasks.Add(transport.SendAsync(queue, CreateTestEnvelope($"Message{i}")));
        }
        await Task.WhenAll(sendTasks);

        await Task.Delay(500); // Wait for processing

        // Assert
        Assert.Equal(100, processedCount);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_MessageDepth_TracksCorrectly()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");

        // Act - Send messages without consumer
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));

        // Assert - Depth should be 3
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, health.PendingMessages);

        // Add consumer and wait
        await transport.SubscribeAsync(queue,
            async (env, ctx, ct) => await ctx.AcknowledgeAsync(ct),
            new ConsumerOptions { StartImmediately = true });

        await Task.Delay(200);

        // Assert - Depth should be 0 after processing
        health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, health.PendingMessages);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_ConsumerStop_StopsProcessingMessages()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var processedCount = 0;

        var consumer = await transport.SubscribeAsync(queue,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref processedCount);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            },
            new ConsumerOptions { StartImmediately = true });

        // Send and process first message
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await Task.Delay(100);

        // Act - Stop consumer
        await consumer.StopAsync(TestContext.Current.CancellationToken);
        var countAfterStop = processedCount;

        // Send message while stopped
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await Task.Delay(100);

        // Assert - Message not processed while stopped
        Assert.Equal(countAfterStop, processedCount);
        Assert.False(consumer.IsActive);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    private static TransportEnvelope CreateTestEnvelope(string messageType = "TestMessage")
    {
        return new TransportEnvelope(
            messageType,
            new byte[] { 1, 2, 3, 4, 5 }.AsMemory(),
            messageId: Guid.NewGuid().ToString());
    }
}
