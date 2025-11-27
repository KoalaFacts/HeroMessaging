using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

/// <summary>
/// Unit tests for InMemoryTopic functionality through InMemoryTransport
/// Since InMemoryTopic is internal, we test its behavior through the public transport API
/// </summary>
[Trait("Category", "Unit")]
public class InMemoryTopicTests
{
    private readonly InMemoryTransportOptions _options;

    public InMemoryTopicTests()
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
    public async Task Topic_PublishWithNoSubscriptions_CompletesSuccessfully()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var envelope = CreateTestEnvelope();

        // Act
        await transport.PublishAsync(topic, envelope);

        // Assert - Should not throw
        var health = await transport.GetHealthAsync();
        Assert.NotNull(health);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_PublishWithSingleSubscription_DeliversMessage()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var receivedMessages = new List<string>();

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                receivedMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        var envelope = CreateTestEnvelope("TestEvent");

        // Act
        await transport.PublishAsync(topic, envelope);
        await Task.Delay(100); // Wait for processing

        // Assert
        Assert.Single(receivedMessages);
        Assert.Equal("TestEvent", receivedMessages[0]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_PublishWithMultipleSubscriptions_DeliversToAll()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();
        var consumer3Messages = new List<string>();

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer1Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer2Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer3Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer3" });

        var envelope = CreateTestEnvelope("BroadcastEvent");

        // Act
        await transport.PublishAsync(topic, envelope);
        await Task.Delay(100); // Wait for processing

        // Assert - All consumers should receive the message
        Assert.Single(consumer1Messages);
        Assert.Equal("BroadcastEvent", consumer1Messages[0]);
        Assert.Single(consumer2Messages);
        Assert.Equal("BroadcastEvent", consumer2Messages[0]);
        Assert.Single(consumer3Messages);
        Assert.Equal("BroadcastEvent", consumer3Messages[0]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_MultiplePublishes_AllSubscribersReceiveAll()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer1Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer2Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        // Act
        await transport.PublishAsync(topic, CreateTestEnvelope("Event1"));
        await transport.PublishAsync(topic, CreateTestEnvelope("Event2"));
        await transport.PublishAsync(topic, CreateTestEnvelope("Event3"));

        await Task.Delay(200); // Wait for processing

        // Assert
        Assert.Equal(3, consumer1Messages.Count);
        Assert.Equal(3, consumer2Messages.Count);
        Assert.Equal("Event1", consumer1Messages[0]);
        Assert.Equal("Event2", consumer1Messages[1]);
        Assert.Equal("Event3", consumer1Messages[2]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_WithSubscriberFailure_DoesNotAffectOtherSubscribers()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var workingConsumerMessages = new List<string>();

        await transport.SubscribeAsync(topic,
            (env, ctx, ct) =>
            {
                throw new InvalidOperationException("Failing consumer");
            },
            new ConsumerOptions
            {
                StartImmediately = true,
                ConsumerId = "failing-consumer",
                MessageRetryPolicy = new RetryPolicy { MaxAttempts = 1 }
            });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                workingConsumerMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "working-consumer" });

        var envelope = CreateTestEnvelope("TestEvent");

        // Act
        await transport.PublishAsync(topic, envelope);
        await Task.Delay(200); // Wait for processing

        // Assert - Working consumer should still receive message
        Assert.Single(workingConsumerMessages);
        Assert.Equal("TestEvent", workingConsumerMessages[0]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_WithAllSubscribersFailure_CompletesSuccessfully()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");

        await transport.SubscribeAsync(topic,
            (env, ctx, ct) => throw new InvalidOperationException("Consumer 1 fails"),
            new ConsumerOptions
            {
                StartImmediately = true,
                ConsumerId = "consumer1",
                MessageRetryPolicy = new RetryPolicy { MaxAttempts = 1 }
            });

        await transport.SubscribeAsync(topic,
            (env, ctx, ct) => throw new InvalidOperationException("Consumer 2 fails"),
            new ConsumerOptions
            {
                StartImmediately = true,
                ConsumerId = "consumer2",
                MessageRetryPolicy = new RetryPolicy { MaxAttempts = 1 }
            });

        var envelope = CreateTestEnvelope("TestEvent");

        // Act & Assert - Should not throw even though all subscribers fail
        await transport.PublishAsync(topic, envelope);
        await Task.Delay(200); // Wait for processing

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_RemoveSubscription_StopsDeliveringToUnsubscribedConsumer()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var consumer1Messages = new List<string>();
        var consumer2Messages = new List<string>();

        var consumer1 = await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer1Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                consumer2Messages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        // Act - Unsubscribe consumer1
        await consumer1.DisposeAsync();

        // Publish after unsubscription
        await transport.PublishAsync(topic, CreateTestEnvelope("AfterUnsubscribe"));
        await Task.Delay(100); // Wait for processing

        // Assert - Only consumer2 should receive the message
        Assert.Empty(consumer1Messages);
        Assert.Single(consumer2Messages);
        Assert.Equal("AfterUnsubscribe", consumer2Messages[0]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_PublishDeliversInParallel()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var deliveryTimes = new System.Collections.Concurrent.ConcurrentBag<DateTimeOffset>();
        var startTime = DateTimeOffset.UtcNow;

        // Create consumers with delays to test parallelism
        for (int i = 0; i < 3; i++)
        {
            var consumerId = $"consumer{i}";
            await transport.SubscribeAsync(topic,
                async (env, ctx, ct) =>
                {
                    await Task.Delay(50, ct); // Simulate work
                    deliveryTimes.Add(DateTimeOffset.UtcNow);
                    await ctx.AcknowledgeAsync(ct);
                },
                new ConsumerOptions { StartImmediately = true, ConsumerId = consumerId });
        }

        var envelope = CreateTestEnvelope("ParallelEvent");

        // Act
        await transport.PublishAsync(topic, envelope);
        var endTime = DateTimeOffset.UtcNow;

        // Assert - If parallel, total time should be ~50ms, not 150ms (3 * 50ms)
        var totalTime = (endTime - startTime).TotalMilliseconds;
        Assert.True(totalTime < 120, $"Expected parallel execution (~50ms), but took {totalTime}ms");

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_MultipleConcurrentPublishes_HandlesCorrectly()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var messageCount = 0;

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref messageCount);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        // Act - Publish multiple messages concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(transport.PublishAsync(topic, CreateTestEnvelope($"Event{i}")));
        }
        await Task.WhenAll(tasks);
        await Task.Delay(200); // Wait for processing

        // Assert
        Assert.Equal(10, messageCount);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_LateSubscriber_DoesNotReceivePreviousMessages()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");

        // Publish before subscribing
        await transport.PublishAsync(topic, CreateTestEnvelope("EarlyEvent"));

        var lateSubscriberMessages = new List<string>();

        // Act - Subscribe after publishing
        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                lateSubscriberMessages.Add(env.MessageType);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        await Task.Delay(100);

        // Publish after subscribing
        await transport.PublishAsync(topic, CreateTestEnvelope("LateEvent"));
        await Task.Delay(100);

        // Assert - Should only receive messages after subscription
        Assert.Single(lateSubscriberMessages);
        Assert.Equal("LateEvent", lateSubscriberMessages[0]);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_WithFastAndSlowConsumers_BothReceiveMessage()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var fastTcs = new TaskCompletionSource<bool>();
        var slowTcs = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                await Task.Delay(10, ct);
                fastTcs.SetResult(true);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "fast" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                await Task.Delay(100, ct);
                slowTcs.SetResult(true);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "slow" });

        var envelope = CreateTestEnvelope("TestEvent");

        // Act
        await transport.PublishAsync(topic, envelope);

        // Wait for both consumers to process the message
        // PublishAsync delivers to channel but doesn't wait for processing
        await Task.WhenAll(
            fastTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            slowTcs.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert - Both should have received and processed the message
        Assert.True(fastTcs.Task.IsCompletedSuccessfully);
        Assert.True(slowTcs.Task.IsCompletedSuccessfully);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Topic_HighThroughput_HandlesManyEventsCorrectly()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, TimeProvider.System);
        await transport.ConnectAsync();

        var topic = TransportAddress.Topic("test-topic");
        var consumer1Count = 0;
        var consumer2Count = 0;

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref consumer1Count);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer1" });

        await transport.SubscribeAsync(topic,
            async (env, ctx, ct) =>
            {
                Interlocked.Increment(ref consumer2Count);
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true, ConsumerId = "consumer2" });

        // Act - Publish many events
        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(transport.PublishAsync(topic, CreateTestEnvelope($"Event{i}")));
        }
        await Task.WhenAll(tasks);
        await Task.Delay(500); // Wait for processing

        // Assert - Each consumer should receive all events
        Assert.Equal(50, consumer1Count);
        Assert.Equal(50, consumer2Count);

        await transport.DisposeAsync();
    }

    private static TransportEnvelope CreateTestEnvelope(string messageType = "TestMessage")
    {
        return new TransportEnvelope(
            messageType,
            new byte[] { 1, 2, 3, 4, 5 }.AsMemory(),
            messageId: Guid.NewGuid().ToString());
    }
}
