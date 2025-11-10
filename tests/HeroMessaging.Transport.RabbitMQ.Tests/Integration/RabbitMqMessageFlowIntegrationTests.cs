using HeroMessaging.Abstractions.Transport;
using System.Collections.Concurrent;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Integration tests for RabbitMQ message send, publish, and consume operations
/// Tests actual message flow through real RabbitMQ broker
/// </summary>
[Trait("Category", "Integration")]
public class RabbitMqMessageFlowIntegrationTests : RabbitMqIntegrationTestBase
{
    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_ToQueue_DeliversMessage()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        var receivedMessages = new ConcurrentBag<TransportEnvelope>();
        var messageReceived = new TaskCompletionSource<bool>();

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedMessages.Add(envelope);
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        var testEnvelope = CreateTestEnvelope("Hello RabbitMQ!");

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            testEnvelope);

        // Wait for message (with timeout)
        var received = await Task.WhenAny(
            messageReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.Same(messageReceived.Task, received);
        Assert.Single(receivedMessages);
        var receivedEnvelope = receivedMessages.First();
        Assert.Equal(testEnvelope.MessageId, receivedEnvelope.MessageId);
        Assert.Equal(testEnvelope.CorrelationId, receivedEnvelope.CorrelationId);
        Assert.Equal(testEnvelope.ContentType, receivedEnvelope.ContentType);
        Assert.Equal("Hello RabbitMQ!", System.Text.Encoding.UTF8.GetString(receivedEnvelope.Body.ToArray()));

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_MultipleMessages_DeliversAllInOrder()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        var receivedMessages = new ConcurrentBag<string>();
        var messageCount = 0;
        var allReceived = new TaskCompletionSource<bool>();

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                var content = System.Text.Encoding.UTF8.GetString(envelope.Body.ToArray());
                receivedMessages.Add(content);

                if (Interlocked.Increment(ref messageCount) == 5)
                {
                    allReceived.TrySetResult(true);
                }
                await Task.CompletedTask;
            });

        // Act - send 5 messages
        for (int i = 1; i <= 5; i++)
        {
            await Transport.SendAsync(
                new TransportAddress(queueName, TransportAddressType.Queue),
                CreateTestEnvelope($"Message {i}"));
        }

        // Wait for all messages
        var received = await Task.WhenAny(
            allReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.Same(allReceived.Task, received);
        Assert.Equal(5, receivedMessages.Count);

        // Cleanup
        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_WithHeaders_PreservesHeaders()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        TransportEnvelope receivedEnvelope = default;
        var messageReceived = new TaskCompletionSource<bool>();

        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                receivedEnvelope = envelope;
                messageReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        var testEnvelope = CreateTestEnvelope()
            .WithHeader("CustomHeader", "CustomValue")
            .WithHeader("Priority", 5);

        // Act
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            testEnvelope);

        await Task.WhenAny(messageReceived.Task, Task.Delay(5000));

        // Assert
        Assert.NotEqual(default(TransportEnvelope), receivedEnvelope);
        Assert.True(receivedEnvelope.Headers.ContainsKey("CustomHeader"));
        Assert.Equal("CustomValue", receivedEnvelope.Headers["CustomHeader"].ToString());

        // Cleanup
        await consumer.DisposeAsync();
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_ToExchange_DeliversToAllBoundQueues()
    {
        // Arrange
        var exchangeName = CreateExchangeName();
        var queue1 = CreateQueueName();
        var queue2 = CreateQueueName();

        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition
        {
            Name = exchangeName,
            Type = ExchangeType.Fanout,
            Durable = true
        });
        topology.AddQueue(new QueueDefinition { Name = queue1, Durable = true });
        topology.AddQueue(new QueueDefinition { Name = queue2, Durable = true });
        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = queue1
        });
        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = queue2
        });

        await Transport!.ConfigureTopologyAsync(topology);

        var queue1Received = new TaskCompletionSource<bool>();
        var queue2Received = new TaskCompletionSource<bool>();

        var consumer1 = await Transport.SubscribeAsync(
            new TransportAddress(queue1, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                queue1Received.TrySetResult(true);
                await Task.CompletedTask;
            });

        var consumer2 = await Transport.SubscribeAsync(
            new TransportAddress(queue2, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                queue2Received.TrySetResult(true);
                await Task.CompletedTask;
            });

        // Act
        await Transport.PublishAsync(
            new TransportAddress(exchangeName, TransportAddressType.Topic),
            CreateTestEnvelope("Broadcast message"));

        // Wait for both queues to receive
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        await Task.WhenAny(Task.WhenAll(queue1Received.Task, queue2Received.Task), timeout);

        // Assert
        Assert.True(queue1Received.Task.IsCompletedSuccessfully);
        Assert.True(queue2Received.Task.IsCompletedSuccessfully);

        // Cleanup
        await consumer1.DisposeAsync();
        await consumer2.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_WithRoutingKey_RoutesToCorrectQueue()
    {
        // Arrange
        var exchangeName = CreateExchangeName();
        var ordersQueue = CreateQueueName();
        var eventsQueue = CreateQueueName();

        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition
        {
            Name = exchangeName,
            Type = ExchangeType.Topic,
            Durable = true
        });
        topology.AddQueue(new QueueDefinition { Name = ordersQueue, Durable = true });
        topology.AddQueue(new QueueDefinition { Name = eventsQueue, Durable = true });
        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = ordersQueue,
            RoutingKey = "order.#"
        });
        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = eventsQueue,
            RoutingKey = "event.#"
        });

        await Transport!.ConfigureTopologyAsync(topology);

        var ordersReceived = new ConcurrentBag<string>();
        var eventsReceived = new ConcurrentBag<string>();
        var orderReceived = new TaskCompletionSource<bool>();
        var eventReceived = new TaskCompletionSource<bool>();

        var consumer1 = await Transport.SubscribeAsync(
            new TransportAddress(ordersQueue, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                ordersReceived.Add(System.Text.Encoding.UTF8.GetString(envelope.Body.ToArray()));
                orderReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        var consumer2 = await Transport.SubscribeAsync(
            new TransportAddress(eventsQueue, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                eventsReceived.Add(System.Text.Encoding.UTF8.GetString(envelope.Body.ToArray()));
                eventReceived.TrySetResult(true);
                await Task.CompletedTask;
            });

        // Act - publish with different routing keys
        var orderEnvelope = CreateTestEnvelope("Order created")
            .WithHeader("RoutingKey", "order.created");

        var eventEnvelope = CreateTestEnvelope("Event occurred")
            .WithHeader("RoutingKey", "event.something");

        await Transport.PublishAsync(
            new TransportAddress(exchangeName, TransportAddressType.Topic),
            orderEnvelope);

        await Transport.PublishAsync(
            new TransportAddress(exchangeName, TransportAddressType.Topic),
            eventEnvelope);

        // Wait for messages
        await Task.WhenAny(
            Task.WhenAll(orderReceived.Task, eventReceived.Task),
            Task.Delay(5000));

        // Assert
        Assert.Single(ordersReceived);
        Assert.Contains("Order created", ordersReceived);
        Assert.Single(eventsReceived);
        Assert.Contains("Event occurred", eventsReceived);

        // Cleanup
        await consumer1.DisposeAsync();
        await consumer2.DisposeAsync();
    }

    #endregion

    #region Consumer Tests

    [Fact]
    public async Task SubscribeAsync_MultipleConsumers_CompeteForMessages()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        var consumer1Messages = new ConcurrentBag<string>();
        var consumer2Messages = new ConcurrentBag<string>();
        var totalReceived = 0;
        var allReceived = new TaskCompletionSource<bool>();

        Func<TransportEnvelope, MessageContext, CancellationToken, Task> HandleMessage(ConcurrentBag<string> bag)
        {
            return async (TransportEnvelope envelope, MessageContext context, CancellationToken ct) =>
            {
                var content = System.Text.Encoding.UTF8.GetString(envelope.Body.ToArray());
                bag.Add(content);

                if (Interlocked.Increment(ref totalReceived) == 10)
                {
                    allReceived.TrySetResult(true);
                }
                await Task.CompletedTask;
            };
        }

        var consumer1 = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            HandleMessage(consumer1Messages));

        var consumer2 = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            HandleMessage(consumer2Messages));

        // Act - send 10 messages
        for (int i = 1; i <= 10; i++)
        {
            await Transport.SendAsync(
                new TransportAddress(queueName, TransportAddressType.Queue),
                CreateTestEnvelope($"Message {i}"));
        }

        await Task.WhenAny(allReceived.Task, Task.Delay(10000));

        // Assert - messages distributed between consumers
        Assert.Equal(10, totalReceived);
        Assert.True(consumer1Messages.Count > 0);
        Assert.True(consumer2Messages.Count > 0);
        Assert.Equal(10, consumer1Messages.Count + consumer2Messages.Count);

        // Cleanup
        await consumer1.DisposeAsync();
        await consumer2.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_AfterStop_StopsReceivingMessages()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        var receivedCount = 0;
        var consumer = await Transport.SubscribeAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            async (envelope, context, ct) =>
            {
                Interlocked.Increment(ref receivedCount);
                await Task.CompletedTask;
            },
            new ConsumerOptions { StartImmediately = true });

        // Send first message
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            CreateTestEnvelope("Message 1"));

        await Task.Delay(500); // Wait for delivery

        // Act - stop consumer
        await consumer.StopAsync();

        var countAfterStop = receivedCount;

        // Send more messages
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            CreateTestEnvelope("Message 2"));

        await Task.Delay(500);

        // Assert - no new messages received after stop
        Assert.Equal(1, countAfterStop);
        Assert.Equal(countAfterStop, receivedCount);

        // Cleanup
        await consumer.DisposeAsync();
    }

    #endregion

    #region Publisher Confirms Tests

    [Fact]
    public async Task SendAsync_WithPublisherConfirms_ConfirmsDelivery()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = queueName, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology);

        // Act & Assert - should not throw with publisher confirms enabled
        await Transport.SendAsync(
            new TransportAddress(queueName, TransportAddressType.Queue),
            CreateTestEnvelope());
    }

    #endregion
}
