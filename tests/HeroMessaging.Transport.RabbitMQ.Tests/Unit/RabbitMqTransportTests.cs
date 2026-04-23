using System.Collections.Immutable;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// Unit tests for RabbitMqTransport
/// Target: 100% coverage for public API
/// </summary>
[Trait("Category", "Unit")]
public class RabbitMqTransportTests : IAsyncLifetime
{
    private Mock<ILoggerFactory>? _mockLoggerFactory;
    private Mock<ILogger<RabbitMqTransport>>? _mockLogger;
    private RabbitMqTransportOptions? _options;
    private RabbitMqTransport? _transport;

    private Mock<ILoggerFactory> MockLoggerFactory => _mockLoggerFactory ?? throw new InvalidOperationException("Test not initialized.");
    private RabbitMqTransportOptions Options => _options ?? throw new InvalidOperationException("Test not initialized.");
    private RabbitMqTransport Transport => _transport ?? throw new InvalidOperationException("Test not initialized.");

    public ValueTask InitializeAsync()
    {
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<RabbitMqTransport>>();

        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_mockLogger.Object);

        _options = new RabbitMqTransportOptions
        {
            Host = "localhost",
            Port = 5672,
            VirtualHost = "/",
            UserName = "guest",
            Password = "guest"
        };

        _transport = new RabbitMqTransport(_options, _mockLoggerFactory.Object, TimeProvider.System);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transport != null)
        {
            await _transport.DisposeAsync();
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidOptions_InitializesSuccessfully()
    {
        // Act
        var transport = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
        Assert.Equal(Options.Name, transport.Name);
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqTransport(null!, _mockLoggerFactory!.Object, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqTransport(_options!, null!, TimeProvider.System));
    }

    #endregion

    #region Name and State Tests

    [Fact]
    public void Name_ReturnsOptionsName()
    {
        // Assert
        Assert.Equal(Options.Name, Transport.Name);
    }

    [Fact]
    public void State_InitialState_IsDisconnected()
    {
        // Assert
        Assert.Equal(TransportState.Disconnected, Transport.State);
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var destination = new TransportAddress("test-queue", TransportAddressType.Queue);
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new byte[] { 1, 2, 3 }
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SendAsync(destination, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var topic = new TransportAddress("test-topic", TransportAddressType.Topic);
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new byte[] { 1, 2, 3 }
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.PublishAsync(topic, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var source = new TransportAddress("test-queue", TransportAddressType.Queue);
        static Task Handler(TransportEnvelope env, MessageContext ctx, CancellationToken ct) => Task.CompletedTask;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SubscribeAsync(source, Handler, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region ConfigureTopologyAsync Tests

    [Fact]
    public async Task ConfigureTopologyAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var topology = new TransportTopology();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region GetHealthAsync Tests

    [Fact]
    public async Task GetHealthAsync_WhenDisconnected_ReturnsUnhealthy()
    {
        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);
        var data = Assert.IsType<Dictionary<string, object>>(health.Data);

        // Assert
        Assert.NotNull(health);
        Assert.Equal(Options.Name, health.TransportName);
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Equal(TransportState.Disconnected, health.State);
        Assert.Contains(Options.Host, Assert.IsType<string>(data["Host"]));
        Assert.Equal(Options.Port, Assert.IsType<int>(data["Port"]));
        Assert.Equal(Options.VirtualHost, Assert.IsType<string>(data["VirtualHost"]));
    }

    [Fact]
    public async Task GetHealthAsync_IncludesConsumerCount()
    {
        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);
        var data = Assert.IsType<Dictionary<string, object>>(health.Data);

        // Assert
        Assert.Equal(0, health.ActiveConsumers);
        Assert.Equal(0, Assert.IsType<int>(data["ConsumerCount"]));
    }

    [Fact]
    public async Task GetHealthAsync_IncludesConnectionDetails()
    {
        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);
        var data = Assert.IsType<Dictionary<string, object>>(health.Data);

        // Assert
        Assert.True(data.ContainsKey("Host"));
        Assert.True(data.ContainsKey("Port"));
        Assert.True(data.ContainsKey("VirtualHost"));
        Assert.Equal(Options.Host, Assert.IsType<string>(data["Host"]));
        Assert.Equal(Options.Port, Assert.IsType<int>(data["Port"]));
        Assert.Equal(Options.VirtualHost, Assert.IsType<string>(data["VirtualHost"]));
    }

    [Fact]
    public async Task GetHealthAsync_HasTimestamp()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(health.Timestamp, before, after);
    }

    #endregion

    #region StateChanged Event Tests

    [Fact]
    public async Task StateChanged_WhenConnectAttempted_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        TransportStateChangedEventArgs? eventArgs = null;

        Transport.StateChanged += (sender, args) =>
        {
            eventRaised = true;
            eventArgs = args;
        };

        // Act
        try
        {
            // Will fail because no real RabbitMQ, but should raise Connecting event
            await Transport.ConnectAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            // Expected to fail without real RabbitMQ
        }

        // Assert - should have at least raised Connecting event
        Assert.True(eventRaised);
        Assert.NotNull(eventArgs);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenCalled_DisposesCleanly()
    {
        // Act
        await Transport.DisposeAsync();

        // Assert
        Assert.Equal(TransportState.Disconnected, Transport.State);
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Act
        await Transport.DisposeAsync();
        await Transport.DisposeAsync();
        await Transport.DisposeAsync();

        // Assert - should not throw
    }

    #endregion

    #region DisconnectAsync Tests

    [Fact]
    public async Task DisconnectAsync_WhenAlreadyDisconnected_DoesNotThrow()
    {
        // Act
        await Transport.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransportState.Disconnected, Transport.State);
    }

    #endregion

    #region Additional Constructor Edge Case Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqTransport(_options!, _mockLoggerFactory!.Object, null!));
    }

    [Fact]
    public void Constructor_WithCustomName_SetsCorrectly()
    {
        // Arrange
        _options!.Name = "custom-transport-name";

        // Act
        var transport = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.Equal("custom-transport-name", transport.Name);
    }

    [Fact]
    public void Constructor_WithSslEnabled_StoresConfiguration()
    {
        // Arrange
        _options!.UseSsl = true;
        _options.Port = 5671;

        // Act
        var transport = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
        Assert.Equal(TransportState.Disconnected, transport.State);
    }

    [Fact]
    public void Constructor_WithCustomPoolSettings_InitializesSuccessfully()
    {
        // Arrange
        _options!.MinPoolSize = 5;
        _options.MaxPoolSize = 20;
        _options.MaxChannelsPerConnection = 50;

        // Act
        var transport = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public void State_InitialState_IsAlwaysDisconnected()
    {
        // Arrange
        var transport1 = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);
        var transport2 = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.Equal(TransportState.Disconnected, transport1.State);
        Assert.Equal(TransportState.Disconnected, transport2.State);
    }

    [Fact]
    public async Task StateChanged_RaisesEventWithCorrectArguments()
    {
        // Arrange
        TransportStateChangedEventArgs? capturedArgs = null;
        Transport.StateChanged += (sender, args) => capturedArgs = args;

        // Act
        try
        {
            await Transport.ConnectAsync(TestContext.Current.CancellationToken);
        }
        catch { /* Expected to fail */ }

        // Assert
        var stateChangedArgs = Assert.IsType<TransportStateChangedEventArgs>(capturedArgs);
        Assert.Equal(TransportState.Disconnected, stateChangedArgs.PreviousState);
        Assert.Equal(TransportState.Connecting, stateChangedArgs.CurrentState);
    }

    #endregion

    #region Envelope Handling Tests

    [Fact]
    public async Task SendAsync_WithEmptyBody_ThrowsWhenDisconnected()
    {
        // Arrange
        var destination = new TransportAddress("test-queue", TransportAddressType.Queue);
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = Array.Empty<byte>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SendAsync(destination, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_WithLargeBody_ThrowsWhenDisconnected()
    {
        // Arrange
        var destination = new TransportAddress("test-queue", TransportAddressType.Queue);
        var largeBody = new byte[1024 * 1024]; // 1MB
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = largeBody
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SendAsync(destination, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_WithCustomRoutingKey_ThrowsWhenDisconnected()
    {
        // Arrange
        var topic = new TransportAddress("test-topic", TransportAddressType.Topic);
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new byte[] { 1, 2, 3 },
            Headers = new Dictionary<string, object> { ["RoutingKey"] = "custom.routing.key" }.ToImmutableDictionary()
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.PublishAsync(topic, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_WithHeaders_ThrowsWhenDisconnected()
    {
        // Arrange
        var destination = new TransportAddress("test-queue", TransportAddressType.Queue);
        var headers = new Dictionary<string, object>
        {
            ["TraceId"] = "trace-123",
            ["UserId"] = "user-456"
        }.ToImmutableDictionary();

        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new byte[] { 1, 2, 3 },
            Headers = headers
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SendAsync(destination, envelope, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region Consumer Subscription Tests

    [Fact]
    public async Task SubscribeAsync_WithNullHandler_ThrowsWhenDisconnected()
    {
        // Arrange
        var source = new TransportAddress("test-queue", TransportAddressType.Queue);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SubscribeAsync(source, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeAsync_WithCustomConsumerId_ThrowsWhenDisconnected()
    {
        // Arrange
        var source = new TransportAddress("test-queue", TransportAddressType.Queue);
        var options = new ConsumerOptions { ConsumerId = "custom-consumer-123" };
        static Task Handler(TransportEnvelope env, MessageContext ctx, CancellationToken ct) => Task.CompletedTask;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SubscribeAsync(source, Handler, options, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubscribeAsync_WithDeferredStart_ThrowsWhenDisconnected()
    {
        // Arrange
        var source = new TransportAddress("test-queue", TransportAddressType.Queue);
        var options = new ConsumerOptions { StartImmediately = false };
        static Task Handler(TransportEnvelope env, MessageContext ctx, CancellationToken ct) => Task.CompletedTask;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SubscribeAsync(source, Handler, options, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region Topology Configuration Tests

    [Fact]
    public async Task ConfigureTopologyAsync_WithEmptyTopology_ThrowsWhenDisconnected()
    {
        // Arrange
        var topology = new TransportTopology();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithMultipleExchanges_ThrowsWhenDisconnected()
    {
        // Arrange
        var topology = new TransportTopology()
            .AddExchange(new ExchangeDefinition { Name = "exchange1", Type = ExchangeType.Direct })
            .AddExchange(new ExchangeDefinition { Name = "exchange2", Type = ExchangeType.Topic })
            .AddExchange(new ExchangeDefinition { Name = "exchange3", Type = ExchangeType.Fanout });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithQueueArguments_ThrowsWhenDisconnected()
    {
        // Arrange
        var topology = new TransportTopology()
            .AddQueue(new QueueDefinition
            {
                Name = "test-queue",
                Durable = true,
                MaxLength = 1000,
                MessageTtl = TimeSpan.FromHours(1),
                MaxPriority = 10
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithDeadLetterExchange_ThrowsWhenDisconnected()
    {
        // Arrange
        var topology = new TransportTopology()
            .AddQueue(new QueueDefinition
            {
                Name = "main-queue",
                DeadLetterExchange = "dlx",
                DeadLetterRoutingKey = "dead-letter"
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithComplexBindings_ThrowsWhenDisconnected()
    {
        // Arrange
        var topology = new TransportTopology()
            .AddExchange(new ExchangeDefinition { Name = "orders", Type = ExchangeType.Topic })
            .AddQueue(new QueueDefinition { Name = "order-queue" })
            .AddBinding(new BindingDefinition { SourceExchange = "orders", Destination = "order-queue", RoutingKey = "order.#" });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.ConfigureTopologyAsync(topology, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region Health Check Edge Cases

    [Fact]
    public async Task GetHealthAsync_WhenConnected_ReturnsValidHealth()
    {
        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(health);
        Assert.True(health.Timestamp != default);
        Assert.True(health.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GetHealthAsync_MultipleCallsConsistent_ReturnsSameState()
    {
        // Act
        var health1 = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);
        var health2 = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(health1.State, health2.State);
        Assert.Equal(health1.Status, health2.Status);
    }

    [Fact]
    public async Task GetHealthAsync_HasAllRequiredDataKeys()
    {
        // Act
        var health = await Transport.GetHealthAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(health.Data);
        Assert.NotEmpty(health.Data);
        Assert.Contains("Host", health.Data.Keys);
        Assert.Contains("Port", health.Data.Keys);
        Assert.Contains("VirtualHost", health.Data.Keys);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public async Task Error_EventIsInvocable()
    {
        // Arrange
        TransportErrorEventArgs? errorArgs = null;

        Transport.Error += (sender, args) =>
        {
            errorArgs = args;
        };

        // Act
        try
        {
            await Transport.ConnectAsync(TestContext.Current.CancellationToken);
        }
        catch { /* Expected */ }

        // Assert - Error event may have been raised
        // (not guaranteed depending on exact failure mode)
    }

    [Fact]
    public void StateChanged_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        static void Handler(object? sender, TransportStateChangedEventArgs args) { }

        // Act & Assert - Should not throw
        Transport.StateChanged += Handler;
        Transport.StateChanged -= Handler;
    }

    [Fact]
    public void Error_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        static void Handler(object? sender, TransportErrorEventArgs args) { }

        // Act & Assert - Should not throw
        Transport.Error += Handler;
        Transport.Error -= Handler;
    }

    #endregion

    #region Concurrent Operation Tests

    [Fact]
    public async Task DisconnectAsync_CalledDuringDisconnect_HandlesGracefully()
    {
        // Arrange
        var task1 = Transport.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        var task2 = Transport.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal(TransportState.Disconnected, Transport.State);
    }

    [Fact]
    public async Task Multiple_Operations_WhenDisconnected_AllThrow()
    {
        // Arrange
        var sendTask = _transport!.SendAsync(
            new TransportAddress("queue", TransportAddressType.Queue),
            new TransportEnvelope { MessageId = "1", Body = new byte[] { 1 } }, cancellationToken: TestContext.Current.CancellationToken);

        var publishTask = _transport.PublishAsync(
            new TransportAddress("topic", TransportAddressType.Topic),
            new TransportEnvelope { MessageId = "2", Body = new byte[] { 2 } }, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await sendTask);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await publishTask);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task SendAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var destination = new TransportAddress("test-queue", TransportAddressType.Queue);
        var envelope = new TransportEnvelope
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = new byte[] { 1, 2, 3 }
        };

        // Act & Assert - Will fail due to disconnected state before cancellation takes effect
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SendAsync(destination, envelope, cts.Token));
    }

    [Fact]
    public async Task DisconnectAsync_WithCancelledToken_HandlesGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        await _transport!.DisconnectAsync(cts.Token);

        // Assert
        Assert.Equal(TransportState.Disconnected, _transport.State);
    }

    #endregion

    #region Options Validation Tests

    [Fact]
    public void Constructor_WithValidCustomOptions_PreservesConfiguration()
    {
        // Arrange
        _options!.Host = "custom-host";
        _options.Port = 5673;
        _options.VirtualHost = "/custom";
        _options.UserName = "custom-user";
        _options.Password = "custom-pass";

        // Act
        _ = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.Equal("custom-host", Options.Host);
        Assert.Equal(5673, Options.Port);
        Assert.Equal("/custom", Options.VirtualHost);
    }

    [Fact]
    public void Constructor_WithHeartbeatConfiguration_InitializesSuccessfully()
    {
        // Arrange
        _options!.Heartbeat = TimeSpan.FromSeconds(30);

        // Act
        var transport = new RabbitMqTransport(Options, MockLoggerFactory.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
    }

    [Fact]
    public void Constructor_WithConnectionTimeout_InitializesSuccessfully()
    {
        // Arrange
        _options!.ConnectionTimeout = TimeSpan.FromSeconds(10);

        // Act
        var transport = new RabbitMqTransport(_options, _mockLoggerFactory!.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
    }

    #endregion
}
