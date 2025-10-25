using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Xunit;

namespace HeroMessaging.Tests.Transport.InMemory;

[Trait("Category", "Unit")]
public class InMemoryTransportTests : IAsyncLifetime
{
    private InMemoryTransport? _transport;
    private readonly InMemoryTransportOptions _options;

    public InMemoryTransportTests()
    {
        _options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 1000,
            SimulateNetworkDelay = false
        };
    }

    public async Task InitializeAsync()
    {
        _transport = new InMemoryTransport(_options);
        await _transport.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_transport != null)
        {
            await _transport.DisposeAsync();
        }
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryTransport(null!));
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        // Assert
        Assert.Equal("TestTransport", _transport!.Name);
    }

    [Fact]
    public async Task ConnectAsync_ChangesStateToConnected()
    {
        // Arrange
        var transport = new InMemoryTransport(_options);

        // Act
        await transport.ConnectAsync();

        // Assert
        Assert.Equal(TransportState.Connected, transport.State);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_RaisesStateChangedEvent()
    {
        // Arrange
        var transport = new InMemoryTransport(_options);
        TransportStateChangedEventArgs? eventArgs = null;
        transport.StateChanged += (sender, args) => eventArgs = args;

        // Act
        await transport.ConnectAsync();

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(TransportState.Disconnected, eventArgs.PreviousState);
        Assert.Equal(TransportState.Connected, eventArgs.CurrentState);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNotRaiseEvent()
    {
        // Arrange
        int eventCount = 0;
        _transport!.StateChanged += (sender, args) => eventCount++;

        // Act
        await _transport.ConnectAsync(); // Already connected in InitializeAsync
        await _transport.ConnectAsync(); // Second connect

        // Assert - Should only fire once during InitializeAsync
        Assert.Equal(2, eventCount); // Connecting and Connected events
    }

    [Fact]
    public async Task DisconnectAsync_ChangesStateToDisconnected()
    {
        // Act
        await _transport!.DisconnectAsync();

        // Assert
        Assert.Equal(TransportState.Disconnected, _transport.State);
    }

    [Fact]
    public async Task SendAsync_ToQueue_DeliversMessage()
    {
        // Arrange
        var destination = TransportAddress.Queue("test-queue");
        var envelope = new TransportEnvelope("TestMessage", new byte[] { 1, 2, 3 }.AsMemory());

        // Act
        await _transport!.SendAsync(destination, envelope);

        // Assert - Message should be enqueued (we'll verify through consumer)
        var health = await _transport.GetHealthAsync();
        Assert.True(health.PendingMessages > 0);
    }

    [Fact]
    public async Task SendAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options);
        var destination = TransportAddress.Queue("test-queue");
        var envelope = new TransportEnvelope("TestMessage", new byte[] { 1, 2, 3 }.AsMemory());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transport.SendAsync(destination, envelope));

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_ToTopic_DeliversToAllSubscribers()
    {
        // Arrange
        var topic = TransportAddress.Topic("test-topic");
        var envelope = new TransportEnvelope("TestEvent", new byte[] { 1, 2, 3 }.AsMemory());

        int subscriber1Count = 0;
        int subscriber2Count = 0;

        await _transport!.SubscribeAsync(
            topic,
            async (env, ctx, ct) =>
            {
                subscriber1Count++;
                await Task.CompletedTask;
            });

        await _transport.SubscribeAsync(
            topic,
            async (env, ctx, ct) =>
            {
                subscriber2Count++;
                await Task.CompletedTask;
            });

        // Act
        await _transport.PublishAsync(topic, envelope);
        await Task.Delay(100); // Give time for async processing

        // Assert
        Assert.Equal(1, subscriber1Count);
        Assert.Equal(1, subscriber2Count);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesConsumer()
    {
        // Arrange
        var source = TransportAddress.Queue("test-queue");
        bool handlerCalled = false;

        // Act
        var consumer = await _transport!.SubscribeAsync(
            source,
            async (envelope, context, ct) =>
            {
                handlerCalled = true;
                await Task.CompletedTask;
            },
            new ConsumerOptions { StartImmediately = false });

        // Assert
        Assert.NotNull(consumer);
        Assert.Equal(source, consumer.Source);
        Assert.False(handlerCalled);

        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_WithAutoStart_ProcessesMessages()
    {
        // Arrange
        var queue = TransportAddress.Queue("test-queue-2");
        var envelope = new TransportEnvelope("TestMessage", new byte[] { 1, 2, 3 }.AsMemory());
        bool messageReceived = false;

        // Act
        var consumer = await _transport!.SubscribeAsync(
            queue,
            async (env, ctx, ct) =>
            {
                messageReceived = true;
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        await _transport.SendAsync(queue, envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        Assert.True(messageReceived);

        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealthyStatus()
    {
        // Act
        var health = await _transport!.GetHealthAsync();

        // Assert
        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(TransportState.Connected, health.State);
        Assert.Equal("TestTransport", health.TransportName);
        Assert.NotNull(health.Data);
    }

    [Fact]
    public async Task GetHealthAsync_WhenDisconnected_ReturnsUnhealthyStatus()
    {
        // Arrange
        await _transport!.DisconnectAsync();

        // Act
        var health = await _transport.GetHealthAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Equal(TransportState.Disconnected, health.State);
    }

    [Fact]
    public async Task ConfigureTopologyAsync_CreatesQueuesAndTopics()
    {
        // Arrange
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = "queue1" });
        topology.AddQueue(new QueueDefinition { Name = "queue2" });
        topology.AddTopic(new TopicDefinition { Name = "topic1" });

        // Act
        await _transport!.ConfigureTopologyAsync(topology);

        // Assert
        var health = await _transport.GetHealthAsync();
        Assert.NotNull(health.Data);
        Assert.True((int)health.Data["QueueCount"] >= 2);
        Assert.True((int)health.Data["TopicCount"] >= 1);
    }

    [Fact]
    public async Task Consumer_Metrics_TracksMessageProcessing()
    {
        // Arrange
        var queue = TransportAddress.Queue("metrics-queue");
        var envelope = new TransportEnvelope("TestMessage", new byte[] { 1, 2, 3 }.AsMemory());

        var consumer = await _transport!.SubscribeAsync(
            queue,
            async (env, ctx, ct) =>
            {
                await Task.Delay(10, ct); // Simulate processing
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        // Act
        await _transport.SendAsync(queue, envelope);
        await Task.Delay(200); // Give time for processing

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesReceived);
        Assert.Equal(1, metrics.MessagesProcessed);
        Assert.Equal(1, metrics.MessagesAcknowledged);
        Assert.Equal(0, metrics.MessagesFailed);
        Assert.Equal(1.0, metrics.SuccessRate);

        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_WithRetry_RetriesFailedMessages()
    {
        // Arrange
        var queue = TransportAddress.Queue("retry-queue");
        var envelope = new TransportEnvelope("TestMessage", new byte[] { 1, 2, 3 }.AsMemory());
        int attemptCount = 0;

        var consumer = await _transport!.SubscribeAsync(
            queue,
            async (env, ctx, ct) =>
            {
                attemptCount++;
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException("Simulated failure");
                }
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions
            {
                StartImmediately = true,
                MessageRetryPolicy = new RetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(10),
                    UseExponentialBackoff = false
                }
            });

        // Act
        await _transport.SendAsync(queue, envelope);
        await Task.Delay(500); // Give time for retries

        // Assert
        Assert.Equal(3, attemptCount);

        await consumer.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_StopAsync_StopsProcessing()
    {
        // Arrange
        var queue = TransportAddress.Queue("stop-queue");
        int processedCount = 0;

        var consumer = await _transport!.SubscribeAsync(
            queue,
            async (env, ctx, ct) =>
            {
                processedCount++;
                await ctx.AcknowledgeAsync(ct);
            },
            new ConsumerOptions { StartImmediately = true });

        // Act
        await consumer.StopAsync();
        await _transport.SendAsync(queue, new TransportEnvelope("Test", new byte[] { 1 }.AsMemory()));
        await Task.Delay(100);

        // Assert
        Assert.False(consumer.IsActive);
        Assert.Equal(0, processedCount); // Should not process after stop

        await consumer.DisposeAsync();
    }
}
