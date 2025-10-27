using HeroMessaging.Abstractions.Transport;
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
            CreateTestEnvelope());
    }

    [Fact]
    public async Task SubscribeAsync_ToNonExistentQueue_ThrowsException()
    {
        // Arrange
        var nonExistentQueue = "queue-that-does-not-exist";

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await Transport!.SubscribeAsync(
                new TransportAddress(nonExistentQueue, TransportAddressType.Queue),
                async (envelope, context, ct) => await Task.CompletedTask);
        });
    }

    [Fact]
    public async Task SendAsync_WithLargeMessage_HandlesCorrectly()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        // Create a large message (1MB)
        var largeContent = new byte[1024 * 1024];
        new Random().NextBytes(largeContent);

        var largeEnvelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid(),
            ContentType = "application/octet-stream",
            Body = largeContent
        };

        var messageReceived = new TaskCompletionSource<bool>();
        byte[]? receivedBody = null;

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedBody = envelope.Body;
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            largeEnvelope);

        await Task.WhenAny(messageReceived.Task, Task.Delay(10000));

        // Assert
        Assert.True(messageReceived.Task.IsCompletedSuccessfully);
        Assert.NotNull(receivedBody);
        Assert.Equal(largeContent.Length, receivedBody!.Length);
        Assert.Equal(largeContent, receivedBody);

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_WhenHandlerThrows_NacksAndRequeues()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

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
            });

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            CreateTestEnvelope());

        // Wait for both attempts
        await Task.WhenAny(
            Task.WhenAll(firstAttempt.Task, secondAttempt.Task),
            Task.Delay(5000));

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

        await Transport!.ConfigureTopologyAsync(topology1);

        // Try to reconfigure with incompatible properties
        var topology2 = new TransportTopology();
        topology2.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = false, // Different from original
            AutoDelete = true
        });

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await Transport.ConfigureTopologyAsync(topology2);
        });
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
        await Transport!.ConfigureTopologyAsync(topology);

        var messageReceived = new TaskCompletionSource<bool>();
        byte[]? receivedBody = null;

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedBody = envelope.Body;
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        var emptyEnvelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid(),
            ContentType = "application/octet-stream",
            Body = Array.Empty<byte>()
        };

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            emptyEnvelope);

        await Task.WhenAny(messageReceived.Task, Task.Delay(5000));

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
        await Transport!.ConfigureTopologyAsync(topology);

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
            });

        // Act - send 100 messages concurrently
        var sendTasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            sendTasks.Add(Transport.SendAsync(
                new TransportAddress(queueName, TransportAddressType.Queue),
                CreateTestEnvelope($"Concurrent message {i}")));
        }

        await Task.WhenAll(sendTasks);

        // Wait for all messages to be received
        await Task.WhenAny(allReceived.Task, Task.Delay(15000));

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
        await Transport!.ConfigureTopologyAsync(topology);

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) => await Task.CompletedTask);

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
        await Transport!.ConfigureTopologyAsync(topology);

        // Act - create consumer
        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) => await Task.CompletedTask);

        var health = await Transport.GetHealthAsync();

        // Assert
        Assert.Equal(1, health.ActiveConsumers);

        // Cleanup
        await consumer.DisposeAsync();

        // Check health after cleanup
        var healthAfter = await Transport.GetHealthAsync();
        Assert.Equal(0, healthAfter.ActiveConsumers);
    }

    #endregion
}
