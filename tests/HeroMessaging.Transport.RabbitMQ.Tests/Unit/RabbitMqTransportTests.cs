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
        var transport = new RabbitMqTransport(_options!, _mockLoggerFactory!.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(transport);
        Assert.Equal(_options.Name, transport.Name);
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
        Assert.Equal(_options!.Name, _transport!.Name);
    }

    [Fact]
    public void State_InitialState_IsDisconnected()
    {
        // Assert
        Assert.Equal(TransportState.Disconnected, _transport!.State);
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
            await _transport!.SendAsync(destination, envelope));
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
            await _transport!.PublishAsync(topic, envelope));
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var source = new TransportAddress("test-queue", TransportAddressType.Queue);
        Task Handler(TransportEnvelope env, MessageContext ctx, CancellationToken ct) => Task.CompletedTask;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transport!.SubscribeAsync(source, Handler));
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
            await _transport!.ConfigureTopologyAsync(topology));
    }

    #endregion

    #region GetHealthAsync Tests

    [Fact]
    public async Task GetHealthAsync_WhenDisconnected_ReturnsUnhealthy()
    {
        // Act
        var health = await _transport!.GetHealthAsync();

        // Assert
        Assert.NotNull(health);
        Assert.Equal(_options!.Name, health.TransportName);
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Equal(TransportState.Disconnected, health.State);
        Assert.Contains(_options.Host, health.Data["Host"].ToString());
        Assert.Equal(_options.Port, health.Data["Port"]);
        Assert.Equal(_options.VirtualHost, health.Data["VirtualHost"]);
    }

    [Fact]
    public async Task GetHealthAsync_IncludesConsumerCount()
    {
        // Act
        var health = await _transport!.GetHealthAsync();

        // Assert
        Assert.Equal(0, health.ActiveConsumers);
        Assert.Equal(0, health.Data["ConsumerCount"]);
    }

    [Fact]
    public async Task GetHealthAsync_IncludesConnectionDetails()
    {
        // Act
        var health = await _transport!.GetHealthAsync();

        // Assert
        Assert.True(health.Data.ContainsKey("Host"));
        Assert.True(health.Data.ContainsKey("Port"));
        Assert.True(health.Data.ContainsKey("VirtualHost"));
        Assert.Equal(_options!.Host, health.Data["Host"]);
        Assert.Equal(_options.Port, health.Data["Port"]);
        Assert.Equal(_options.VirtualHost, health.Data["VirtualHost"]);
    }

    [Fact]
    public async Task GetHealthAsync_HasTimestamp()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var health = await _transport!.GetHealthAsync();

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

        _transport!.StateChanged += (sender, args) =>
        {
            eventRaised = true;
            eventArgs = args;
        };

        // Act
        try
        {
            // Will fail because no real RabbitMQ, but should raise Connecting event
            await _transport.ConnectAsync();
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
        await _transport!.DisposeAsync();

        // Assert
        Assert.Equal(TransportState.Disconnected, _transport.State);
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Act
        await _transport!.DisposeAsync();
        await _transport.DisposeAsync();
        await _transport.DisposeAsync();

        // Assert - should not throw
    }

    #endregion

    #region DisconnectAsync Tests

    [Fact]
    public async Task DisconnectAsync_WhenAlreadyDisconnected_DoesNotThrow()
    {
        // Act
        await _transport!.DisconnectAsync();

        // Assert
        Assert.Equal(TransportState.Disconnected, _transport.State);
    }

    #endregion
}
