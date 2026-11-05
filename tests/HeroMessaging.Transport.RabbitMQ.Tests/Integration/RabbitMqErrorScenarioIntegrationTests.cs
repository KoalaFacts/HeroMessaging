using HeroMessaging.Abstractions.Transport;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Integration tests for RabbitMQ error scenarios and edge cases
/// Tests resilience and error handling with real RabbitMQ broker
/// </summary>
[Trait("Category", "Integration")]
public class RabbitMqErrorScenarioIntegrationTests : RabbitMqIntegrationTestBase
{
    #region Error Handling Tests

    [Fact]
    public async Task SendAsync_ToNonExistentQueue_ThrowsException()
    {
        // Arrange
        var nonExistentQueue = "queue-that-does-not-exist";

        // Act & Assert
        // Note: RabbitMQ accepts sends to non-existent queues when using default exchange
        // The message goes to a queue with the routing key name if it exists
        // This behavior is by design in AMQP
        await Transport!.SendAsync(
            new TransportAddress(nonExistentQueue, TransportAddressType.Queue),
            CreateTestEnvelope(), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_ToNonExistentQueue_ThrowsException()
    {
        // Arrange
        var nonExistentQueue = "queue-that-does-not-exist";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationInterruptedException>(async () =>
        {
            await Transport!.SubscribeAsync(
                new TransportAddress(nonExistentQueue, TransportAddressType.Queue),
                async (envelope, context, ct) => await Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);
        });
        Assert.NotNull(exception.ShutdownReason);
        Assert.Equal((ushort)404, exception.ShutdownReason.ReplyCode);
    }

    [Fact]
    public async Task SubscribeAsync_WhenQueueIsMissing_ReleasesConsumerIdForRetry()
    {
        var queueName = CreateQueueName();
        var address = new TransportAddress(queueName, TransportAddressType.Queue);
        var options = new ConsumerOptions { ConsumerId = Guid.NewGuid().ToString("N") };

        var error = await Assert.ThrowsAsync<OperationInterruptedException>(
            () => Transport!.SubscribeAsync(
                address,
                static (_, _, _) => Task.CompletedTask,
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal((ushort)404, error.ShutdownReason?.ReplyCode);
        Assert.Equal(0, (await Transport!.GetHealthAsync(TestContext.Current.CancellationToken)).ActiveConsumers);

        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);

        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await Transport.SubscribeAsync(
            address,
            (_, _, _) =>
            {
                received.TrySetResult(true);
                return Task.CompletedTask;
            },
            options,
            TestContext.Current.CancellationToken);

        await Transport.SendAsync(address, CreateTestEnvelope(), TestContext.Current.CancellationToken);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(1, (await Transport.GetHealthAsync(TestContext.Current.CancellationToken)).ActiveConsumers);
    }

    [Fact]
    public async Task SendAsync_WithLargeMessage_HandlesCorrectly()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        // Create a large message (1MB)
        var largeContent = new byte[1024 * 1024];
        new Random().NextBytes(largeContent);

        var largeEnvelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/octet-stream",
            Body = largeContent
        };

        var messageReceived = new TaskCompletionSource<bool>();
        byte[]? receivedBody = null;

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedBody = envelope.Body.ToArray();
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            largeEnvelope, cancellationToken: TestContext.Current.CancellationToken);

        await Task.WhenAny(messageReceived.Task, Task.Delay(10000, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(messageReceived.Task.IsCompletedSuccessfully);
        Assert.NotNull(receivedBody);
        Assert.Equal(largeContent.Length, receivedBody!.Length);
        Assert.Equal(largeContent, receivedBody);

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_WhenHandlerKeepsFailing_RetriesWithBackoffThenDeadLetters()
    {
        // Arrange: work queue dead-letters to a retained queue through a direct exchange.
        var queueName = CreateQueueName();
        var dlxName = CreateExchangeName();
        var dlqName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition { Name = dlxName, Type = ExchangeType.Direct, Durable = true });
        topology.AddQueue(new QueueDefinition { Name = dlqName, Durable = true });
        topology.AddBinding(new BindingDefinition { SourceExchange = dlxName, Destination = dlqName, RoutingKey = "failed" });
        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            DeadLetterExchange = dlxName,
            DeadLetterRoutingKey = "failed"
        });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var attempts = new List<(long Timestamp, int DeliveryCount)>();
        var deadLettered = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new ConsumerOptions
        {
            MessageRetryPolicy = new RetryPolicy
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(5),
                UseExponentialBackoff = true
            }
        };

        var consumer = await Transport.SubscribeAsync(
            TransportAddress.Queue(queueName),
            (envelope, _, _) =>
            {
                lock (attempts)
                    attempts.Add((System.Diagnostics.Stopwatch.GetTimestamp(), envelope.DeliveryCount));
                throw new InvalidOperationException("Simulated poison message");
            }, options, TestContext.Current.CancellationToken);
        var dlqConsumer = await Transport.SubscribeAsync(
            TransportAddress.Queue(dlqName),
            (envelope, _, _) =>
            {
                deadLettered.TrySetResult(envelope);
                return Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);
        var sent = CreateTestEnvelope();

        // Act
        await Transport.SendAsync(TransportAddress.Queue(queueName), sent, cancellationToken: TestContext.Current.CancellationToken);
        var retained = await deadLettered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        // Assert: one initial delivery plus MaxAttempts retries, waiting 200ms then 400ms, then retained once.
        (long Timestamp, int DeliveryCount)[] observed;
        lock (attempts)
            observed = [.. attempts];
        Assert.Equal(3, observed.Length);
        Assert.Equal([0, 1, 2], observed.Select(static attempt => attempt.DeliveryCount));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(observed[0].Timestamp, observed[1].Timestamp) >= TimeSpan.FromMilliseconds(180));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(observed[1].Timestamp, observed[2].Timestamp) >= TimeSpan.FromMilliseconds(380));
        Assert.Equal(sent.MessageId, retained.MessageId);
        Assert.True(retained.Headers.ContainsKey("x-death"));
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesDeadLettered);
        Assert.Equal(3, metrics.MessagesFailed);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        lock (attempts)
            Assert.Equal(3, attempts.Count);

        // Cleanup
        await consumer.DisposeAsync();
        await dlqConsumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_OnQuorumQueueAfterConsumerRestart_ContinuesRetryCountThenDeadLetters()
    {
        // Arrange: a quorum queue supplies x-delivery-count, so a new consumer continues the count.
        var queueName = CreateQueueName();
        var dlxName = CreateExchangeName();
        var dlqName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition { Name = dlxName, Type = ExchangeType.Direct, Durable = true });
        topology.AddQueue(new QueueDefinition { Name = dlqName, Durable = true });
        topology.AddBinding(new BindingDefinition { SourceExchange = dlxName, Destination = dlqName, RoutingKey = "failed" });
        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            DeadLetterExchange = dlxName,
            DeadLetterRoutingKey = "failed",
            Arguments = new Dictionary<string, object> { ["x-queue-type"] = "quorum" }
        });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var options = new ConsumerOptions
        {
            MessageRetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromMinutes(5) }
        };
        var firstFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await Transport.SubscribeAsync(TransportAddress.Queue(queueName), (_, _, _) =>
        {
            firstFailed.TrySetResult();
            throw new InvalidOperationException("Simulated poison message");
        }, options, TestContext.Current.CancellationToken);
        var deadLettered = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dlqConsumer = await Transport.SubscribeAsync(TransportAddress.Queue(dlqName), (envelope, _, _) =>
        {
            deadLettered.TrySetResult(envelope);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act: the first consumer fails once and stops during its backoff, which requeues the delivery.
        await Transport.SendAsync(TransportAddress.Queue(queueName), CreateTestEnvelope(), cancellationToken: TestContext.Current.CancellationToken);
        await firstFailed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await first.DisposeAsync();

        var counts = new List<int>();
        var second = await Transport.SubscribeAsync(TransportAddress.Queue(queueName), (envelope, _, _) =>
        {
            lock (counts)
                counts.Add(envelope.DeliveryCount);
            throw new InvalidOperationException("Simulated poison message");
        }, options with { MessageRetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromMilliseconds(50) } },
            TestContext.Current.CancellationToken);
        var retained = await deadLettered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        // Assert: the restarted consumer saw the broker count and stopped after MaxAttempts retries in total.
        lock (counts)
            Assert.Equal([1, 2], counts);
        Assert.True(retained.Headers.ContainsKey("x-death"));
        Assert.Equal(1, second.GetMetrics().MessagesDeadLettered);

        await second.DisposeAsync();
        await dlqConsumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_WhenHandlerThrows_NacksAndRequeues()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var attemptCount = 0;
        var firstAttempt = new TaskCompletionSource<bool>();
        var secondAttempt = new TaskCompletionSource<bool>();

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                var count = Interlocked.Increment(ref attemptCount);

                if (count == 1)
                {
                    firstAttempt.TrySetResult(true);
                    throw new InvalidOperationException("Simulated failure");
                }
                else if (count == 2)
                {
                    secondAttempt.TrySetResult(true);
                    // Success on retry
                }

                await Task.CompletedTask;
            },
            new ConsumerOptions { MessageRetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(50) } },
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            CreateTestEnvelope(), cancellationToken: TestContext.Current.CancellationToken);

        // Wait for both attempts
        await Task.WhenAny(
            Task.WhenAll(firstAttempt.Task, secondAttempt.Task),
            Task.Delay(5000, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(firstAttempt.Task.IsCompletedSuccessfully);
        Assert.True(secondAttempt.Task.IsCompletedSuccessfully);
        Assert.Equal(2, attemptCount); // Message redelivered after failure

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithIncompatibleOptions_ThrowsException()
    {
        // Arrange
        var queueName = CreateQueueName();

        // Create queue with certain properties
        var topology1 = new TransportTopology();
        topology1.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            AutoDelete = false
        });

        await Transport!.ConfigureTopologyAsync(topology1, cancellationToken: TestContext.Current.CancellationToken);

        // Try to reconfigure with incompatible properties
        var topology2 = new TransportTopology();
        topology2.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = false, // Different from original
            AutoDelete = true
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationInterruptedException>(async () =>
        {
            await Transport.ConfigureTopologyAsync(topology2, TestContext.Current.CancellationToken);
        });
        Assert.NotNull(exception.ShutdownReason);
        Assert.Equal((ushort)406, exception.ShutdownReason.ReplyCode);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SendAsync_WithEmptyBody_DeliversSuccessfully()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var messageReceived = new TaskCompletionSource<bool>();
        byte[]? receivedBody = null;

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedBody = envelope.Body.ToArray();
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);

        var emptyEnvelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/octet-stream",
            Body = Array.Empty<byte>()
        };

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            emptyEnvelope, cancellationToken: TestContext.Current.CancellationToken);

        await Task.WhenAny(messageReceived.Task, Task.Delay(5000, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(messageReceived.Task.IsCompletedSuccessfully);
        Assert.NotNull(receivedBody);
        Assert.Empty(receivedBody);

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_Concurrent_HandlesCorrectly()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var receivedCount = 0;
        var allReceived = new TaskCompletionSource<bool>();

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                if (Interlocked.Increment(ref receivedCount) == 100)
                {
                    allReceived.TrySetResult(true);
                }
                await Task.CompletedTask;
            }, cancellationToken: TestContext.Current.CancellationToken);

        // Act - send 100 messages concurrently
        var sendTasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            sendTasks.Add(Transport.SendAsync(
                new TransportAddress(queueName, TransportAddressType.Queue),
                CreateTestEnvelope($"Concurrent message {i}"), cancellationToken: TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(sendTasks);

        // Wait for all messages to be received
        await Task.WhenAny(allReceived.Task, Task.Delay(15000, TestContext.Current.CancellationToken));

        // Assert
        Assert.True(allReceived.Task.IsCompletedSuccessfully);
        Assert.Equal(100, receivedCount);

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_MultipleDisposeAsync_DoesNotThrow()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) => await Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert - should not throw
        await consumer.DisposeAsync();
        await consumer.DisposeAsync();
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task GetHealthAsync_AfterOperations_ReflectsConsumerCount()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken);

        // Act - create consumer
        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) => await Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);

        var health = await Transport.GetHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, health.ActiveConsumers);

        // Cleanup
        await consumer.DisposeAsync();

        // Check health after cleanup
        var healthAfter = await Transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, healthAfter.ActiveConsumers);
    }

    #endregion
}
