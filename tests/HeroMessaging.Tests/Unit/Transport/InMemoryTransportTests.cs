using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

[Trait("Category", "Unit")]
public class InMemoryTransportTests
{
    private readonly InMemoryTransportOptions _options;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<ITransportInstrumentation> _instrumentationMock;

    public InMemoryTransportTests()
    {
        _options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 100,
            DropWhenFull = false,
            SimulateNetworkDelay = false
        };
        _timeProvider = new FakeTimeProvider();
        _instrumentationMock = new Mock<ITransportInstrumentation>();
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var transport = new InMemoryTransport(_options, _timeProvider, _instrumentationMock.Object);

        // Assert
        Assert.NotNull(transport);
        Assert.Equal("TestTransport", transport.Name);
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryTransport(null!, _timeProvider, _instrumentationMock.Object));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryTransport(_options, null!, _instrumentationMock.Object));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullInstrumentation_UsesNoOpInstrumentation()
    {
        // Act
        var transport = new InMemoryTransport(_options, _timeProvider, null);

        // Assert
        Assert.NotNull(transport);
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);

        // Act
        var name = transport.Name;

        // Assert
        Assert.Equal("TestTransport", name);
    }

    [Fact]
    public void State_InitialState_IsDisconnected()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);

        // Act
        var state = transport.State;

        // Assert
        Assert.Equal(TransportState.Disconnected, state);
    }

    [Fact]
    public async Task ConnectAsync_WhenDisconnected_ChangesStateToConnected()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);

        // Act
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransportState.Connected, transport.State);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConnectAsync_WhenDisconnected_RaisesStateChangedEvent()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        TransportStateChangedEventArgs? eventArgs = null;
        transport.StateChanged += (sender, args) => eventArgs = args;

        // Act
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(TransportState.Disconnected, eventArgs.PreviousState);
        Assert.Equal(TransportState.Connected, eventArgs.CurrentState);
        Assert.NotNull(eventArgs.Reason);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNotRaiseEvent()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        int eventCount = 0;
        transport.StateChanged += (sender, args) => eventCount++;

        // Act
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, eventCount);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConnectAsync_WithNetworkDelaySimulation_ChangesStateThroughConnecting()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            SimulateNetworkDelay = true,
            SimulatedDelayMin = TimeSpan.FromMilliseconds(10),
            SimulatedDelayMax = TimeSpan.FromMilliseconds(20)
        };
        var transport = new InMemoryTransport(options, _timeProvider);

        var stateChanges = new List<TransportState>();
        transport.StateChanged += (sender, args) => stateChanges.Add(args.CurrentState);

        // Act
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransportState.Connected, transport.State);
        Assert.Contains(TransportState.Connecting, stateChanges);
        Assert.Contains(TransportState.Connected, stateChanges);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConnectAsync_WithCancellationToken_CanBeCancelled()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            SimulateNetworkDelay = true,
            SimulatedDelayMin = TimeSpan.FromSeconds(10),
            SimulatedDelayMax = TimeSpan.FromSeconds(20)
        };
        var transport = new InMemoryTransport(options, _timeProvider);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.ConnectAsync(cts.Token, TestContext.Current.CancellationToken));

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_ChangesStateToDisconnected()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await transport.DisconnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_RaisesStateChangedEvents()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var stateChanges = new List<TransportState>();
        transport.StateChanged += (sender, args) => stateChanges.Add(args.CurrentState);

        // Act
        await transport.DisconnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(TransportState.Disconnecting, stateChanges);
        Assert.Contains(TransportState.Disconnected, stateChanges);
    }

    [Fact]
    public async Task DisconnectAsync_ClearsQueuesTopicsAndConsumers()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var topic = TransportAddress.Topic("test-topic");

        await transport.SendAsync(queue, CreateTestEnvelope(), CancellationToken.None);
        await transport.PublishAsync(topic, CreateTestEnvelope(), CancellationToken.None);
        await transport.SubscribeAsync(queue, (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = false });

        // Act
        await transport.DisconnectAsync(TestContext.Current.CancellationToken);

        // Assert
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, health.Data["QueueCount"]);
        Assert.Equal(0, health.Data["TopicCount"]);
        Assert.Equal(0, health.Data["ConsumerCount"]);
    }

    [Fact]
    public async Task SendAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        var destination = TransportAddress.Queue("test-queue");
        var envelope = CreateTestEnvelope();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SendAsync(destination, envelope, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WhenConnected_EnqueuesMessage()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var destination = TransportAddress.Queue("test-queue");
        var envelope = CreateTestEnvelope();

        // Act
        await transport.SendAsync(destination, envelope, TestContext.Current.CancellationToken);

        // Assert
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.True(health.PendingMessages > 0);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_WithInstrumentation_RecordsSendOperation()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider, _instrumentationMock.Object);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var destination = TransportAddress.Queue("test-queue");
        var envelope = CreateTestEnvelope();

        // Act
        await transport.SendAsync(destination, envelope, TestContext.Current.CancellationToken);

        // Assert
        _instrumentationMock.Verify(x => x.StartSendActivity(envelope, destination.Name, "TestTransport"), Times.Once);
        _instrumentationMock.Verify(x => x.RecordOperation("TestTransport", "send", "success"), Times.Once);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_WithQueueFull_ThrowsWhenDropWhenFullIsFalse()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 2,
            DropWhenFull = false
        };
        var transport = new InMemoryTransport(options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var destination = TransportAddress.Queue("test-queue");

        // Fill the queue
        await transport.SendAsync(destination, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(destination, CreateTestEnvelope(, TestContext.Current.CancellationToken));

        // Act & Assert - Third message should fail with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.SendAsync(destination, CreateTestEnvelope(), cts.Token));

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_WithQueueFullAndDropWhenFull_DropsOldestMessage()
    {
        // Arrange
        var options = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 2,
            DropWhenFull = true
        };
        var transport = new InMemoryTransport(options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var destination = TransportAddress.Queue("test-queue");

        // Act - Fill the queue and send one more
        await transport.SendAsync(destination, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(destination, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SendAsync(destination, CreateTestEnvelope(, TestContext.Current.CancellationToken)); // Should drop oldest

        // Assert - Should not throw
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.True(health.PendingMessages <= 2);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        var topic = TransportAddress.Topic("test-topic");
        var envelope = CreateTestEnvelope();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.PublishAsync(topic, envelope, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishAsync_WhenConnected_PublishesToTopic()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var topic = TransportAddress.Topic("test-topic");
        var envelope = CreateTestEnvelope();

        // Act
        await transport.PublishAsync(topic, envelope, TestContext.Current.CancellationToken);

        // Assert
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.True((int)health.Data["TopicCount"] >= 1);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishAsync_WithInstrumentation_RecordsPublishOperation()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider, _instrumentationMock.Object);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var topic = TransportAddress.Topic("test-topic");
        var envelope = CreateTestEnvelope();

        // Act
        await transport.PublishAsync(topic, envelope, TestContext.Current.CancellationToken);

        // Assert
        _instrumentationMock.Verify(x => x.StartPublishActivity(envelope, topic.Name, "TestTransport"), Times.Once);
        _instrumentationMock.Verify(x => x.RecordOperation("TestTransport", "publish", "success"), Times.Once);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        var source = TransportAddress.Queue("test-queue");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SubscribeAsync(source, (env, ctx, ct) => Task.CompletedTask));
        Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribeAsync_ToQueue_CreatesConsumer()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("test-queue");

        // Act
        var consumer = await transport.SubscribeAsync(
            source,
            (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = false });

        // Assert
        Assert.NotNull(consumer);
        Assert.Equal(source, consumer.Source);
        Assert.NotNull(consumer.ConsumerId);
        Assert.False(consumer.IsActive);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_ToTopic_CreatesConsumer()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Topic("test-topic");

        // Act
        var consumer = await transport.SubscribeAsync(
            source,
            (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = false });

        // Assert
        Assert.NotNull(consumer);
        Assert.Equal(source, consumer.Source);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_WithStartImmediately_StartsConsumer()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("test-queue");

        // Act
        var consumer = await transport.SubscribeAsync(
            source,
            (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = true });

        // Assert
        Assert.True(consumer.IsActive);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_WithDuplicateConsumerId_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var source = TransportAddress.Queue("test-queue");
        var consumerId = "duplicate-id";
        var options = new ConsumerOptions { ConsumerId = consumerId, StartImmediately = false };

        await transport.SubscribeAsync(source, (env, ctx, ct) => Task.CompletedTask, options);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.SubscribeAsync(source, (env, ctx, ct) => Task.CompletedTask, options));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        var topology = new TransportTopology();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken));
        Assert.Contains("not connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigureTopologyAsync_CreatesQueuesAndTopics()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = "queue1" });
        topology.AddQueue(new QueueDefinition { Name = "queue2" });
        topology.AddTopic(new TopicDefinition { Name = "topic1" });
        topology.AddTopic(new TopicDefinition { Name = "topic2" });

        // Act
        await transport.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);

        // Assert
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.True((int)health.Data["QueueCount"] >= 2);
        Assert.True((int)health.Data["TopicCount"] >= 2);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetHealthAsync_WhenDisconnected_ReturnsUnhealthyStatus()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);

        // Act
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Equal(TransportState.Disconnected, health.State);
        Assert.Equal("TestTransport", health.TransportName);
    }

    [Fact]
    public async Task GetHealthAsync_WhenConnected_ReturnsHealthyStatus()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(TransportState.Connected, health.State);
        Assert.Equal("TestTransport", health.TransportName);
        Assert.NotNull(health.StatusMessage);
        Assert.Equal(_timeProvider.GetUtcNow(), health.Timestamp);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealthDataWithMetrics()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        await transport.SendAsync(queue, CreateTestEnvelope(, TestContext.Current.CancellationToken));
        await transport.SubscribeAsync(queue, (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = false });

        // Act
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(health.Data);
        Assert.True(health.Data.ContainsKey("QueueCount"));
        Assert.True(health.Data.ContainsKey("TopicCount"));
        Assert.True(health.Data.ContainsKey("ConsumerCount"));
        Assert.Equal(1, health.ActiveConsumers);
        Assert.True(health.PendingMessages > 0);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_DisconnectsTransport()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await transport.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public async Task DisposeAsync_StopsAllConsumers()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var consumer = await transport.SubscribeAsync(
            queue,
            (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = true });

        // Act
        await transport.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task StateChanged_RaisesEventWithCorrectData()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        var stateChangeEvents = new List<TransportStateChangedEventArgs>();
        transport.StateChanged += (sender, args) => stateChangeEvents.Add(args);

        // Act
        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await transport.DisconnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(stateChangeEvents);
        var connectEvent = stateChangeEvents.First(e => e.CurrentState == TransportState.Connected);
        Assert.Equal(TransportState.Disconnected, connectEvent.PreviousState);

        var disconnectEvent = stateChangeEvents.First(e => e.CurrentState == TransportState.Disconnected);
        Assert.Equal(TransportState.Disconnecting, disconnectEvent.PreviousState);
    }

    [Fact]
    public async Task ConsumerDisposal_RemovesConsumerFromTransport()
    {
        // Arrange
        var transport = new InMemoryTransport(_options, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var queue = TransportAddress.Queue("test-queue");
        var consumer = await transport.SubscribeAsync(
            queue,
            (env, ctx, ct) => Task.CompletedTask,
            new ConsumerOptions { StartImmediately = false });

        var healthBefore = await transport.GetHealthAsync(TestContext.Current.CancellationToken);

        // Act
        await consumer.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        var healthAfter = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(healthBefore.ActiveConsumers - 1, healthAfter.ActiveConsumers);

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
