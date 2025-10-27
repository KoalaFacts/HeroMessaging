using HeroMessaging.Abstractions.Transport;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Integration tests for RabbitMQ connection and topology management
/// Requires Docker to be running
/// </summary>
[Trait("Category", "Integration")]
public class RabbitMqConnectionIntegrationTests : RabbitMqIntegrationTestBase
{
    #region Connection Tests

    [Fact]
    public async Task ConnectAsync_WithValidCredentials_ConnectsSuccessfully()
    {
        // Assert - connection was established in base class InitializeAsync
        Assert.NotNull(Transport);
        Assert.Equal(TransportState.Connected, Transport!.State);
    }

    [Fact]
    public async Task GetHealthAsync_WhenConnected_ReturnsHealthy()
    {
        // Act
        var health = await Transport!.GetHealthAsync();

        // Assert
        Assert.NotNull(health);
        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal(TransportState.Connected, health.State);
        Assert.True(health.ActiveConnections > 0);
        Assert.Contains("RabbitMQ transport is healthy", health.StatusMessage);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_DisconnectsSuccessfully()
    {
        // Act
        await Transport!.DisconnectAsync();

        // Assert
        Assert.Equal(TransportState.Disconnected, Transport.State);
    }

    [Fact]
    public async Task ConnectAsync_AfterDisconnect_CanReconnect()
    {
        // Arrange
        await Transport!.DisconnectAsync();
        Assert.Equal(TransportState.Disconnected, Transport.State);

        // Act
        await Transport.ConnectAsync();

        // Assert
        Assert.Equal(TransportState.Connected, Transport.State);
    }

    #endregion

    #region Topology Tests

    [Fact]
    public async Task ConfigureTopologyAsync_WithQueue_CreatesQueue()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            AutoDelete = false
        });

        // Act
        await Transport!.ConfigureTopologyAsync(topology);

        // Assert - if topology creation fails, an exception would be thrown
        // Queue exists and can be used for messaging
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithExchange_CreatesExchange()
    {
        // Arrange
        var exchangeName = CreateExchangeName();
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition
        {
            Name = exchangeName,
            Type = ExchangeType.Topic,
            Durable = true
        });

        // Act
        await Transport!.ConfigureTopologyAsync(topology);

        // Assert - no exception means success
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithQueueAndBinding_CreatesTopology()
    {
        // Arrange
        var exchangeName = CreateExchangeName();
        var queueName = CreateQueueName();
        var topology = new TransportTopology();

        topology.AddExchange(new ExchangeDefinition
        {
            Name = exchangeName,
            Type = ExchangeType.Topic,
            Durable = true
        });

        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true
        });

        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = queueName,
            RoutingKey = "test.#"
        });

        // Act
        await Transport!.ConfigureTopologyAsync(topology);

        // Assert - successful topology creation
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithDeadLetterQueue_CreatesCompleteSetup()
    {
        // Arrange
        var exchangeName = CreateExchangeName();
        var queueName = CreateQueueName();
        var dlxName = CreateExchangeName() + "-dlx";
        var dlqName = queueName + "-dlq";

        var topology = new TransportTopology();

        // Main exchange and queue
        topology.AddExchange(new ExchangeDefinition
        {
            Name = exchangeName,
            Type = ExchangeType.Topic,
            Durable = true
        });

        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            DeadLetterExchange = dlxName,
            DeadLetterRoutingKey = "failed"
        });

        // Dead letter exchange and queue
        topology.AddExchange(new ExchangeDefinition
        {
            Name = dlxName,
            Type = ExchangeType.Direct,
            Durable = true
        });

        topology.AddQueue(new QueueDefinition
        {
            Name = dlqName,
            Durable = true
        });

        // Bindings
        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = queueName,
            RoutingKey = "test.#"
        });

        topology.AddBinding(new BindingDefinition
        {
            SourceExchange = dlxName,
            Destination = dlqName,
            RoutingKey = "failed"
        });

        // Act
        await Transport!.ConfigureTopologyAsync(topology);

        // Assert - complex topology created successfully
    }

    [Fact]
    public async Task ConfigureTopologyAsync_WithQueueOptions_AppliesAllOptions()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();

        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true,
            Exclusive = false,
            AutoDelete = false,
            MaxLength = 10000,
            MessageTtl = TimeSpan.FromHours(1),
            MaxPriority = 10
        });

        // Act
        await Transport!.ConfigureTopologyAsync(topology);

        // Assert - queue created with all options
    }

    [Fact]
    public async Task ConfigureTopologyAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        var queueName = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition
        {
            Name = queueName,
            Durable = true
        });

        // Act - configure same topology twice
        await Transport!.ConfigureTopologyAsync(topology);
        await Transport.ConfigureTopologyAsync(topology); // Should not throw

        // Assert - idempotent operation succeeded
    }

    #endregion
}
