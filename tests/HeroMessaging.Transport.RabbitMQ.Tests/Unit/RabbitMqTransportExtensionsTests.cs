using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// Unit tests for RabbitMQ configuration extensions
/// Target: 100% coverage for configuration API
/// </summary>
[Trait("Category", "Unit")]
public class RabbitMqTransportExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly Mock<IHeroMessagingBuilder> _mockBuilder;

    public RabbitMqTransportExtensionsTests()
    {
        _services = new ServiceCollection();
        _mockBuilder = new Mock<IHeroMessagingBuilder>();
        _mockBuilder.Setup(b => b.Services).Returns(_services);
    }

    #region WithRabbitMq - Simple Overload Tests

    [Fact]
    public void WithRabbitMq_WithHost_RegistersTransport()
    {
        // Act
        var result = _mockBuilder.Object.WithRabbitMq("localhost");

        // Assert
        Assert.NotNull(result);
        var serviceDescriptor = _services.FirstOrDefault(sd => sd.ServiceType == typeof(IMessageTransport));
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, serviceDescriptor.Lifetime);
    }

    [Fact]
    public void WithRabbitMq_WithHostAndConfigure_AppliesConfiguration()
    {
        // Arrange
        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMq("testhost", options =>
        {
            capturedOptions = options;
            options.Port = 5673;
            options.UserName = "testuser";
            options.Password = "testpass";
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal("testhost", capturedOptions.Host);
        Assert.Equal(5673, capturedOptions.Port);
        Assert.Equal("testuser", capturedOptions.UserName);
        Assert.Equal("testpass", capturedOptions.Password);
    }

    [Fact]
    public void WithRabbitMq_WithHost_UsesDefaultPort()
    {
        // Arrange
        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMq("localhost", options =>
        {
            capturedOptions = options;
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(5672, capturedOptions.Port); // Default RabbitMQ port
    }

    #endregion

    #region WithRabbitMq - Full Configuration Overload Tests

    [Fact]
    public void WithRabbitMq_WithFullConfiguration_RegistersTransportWithAllSettings()
    {
        // Arrange
        const string host = "rabbitmq.example.com";
        const int port = 5673;
        const string vhost = "/production";
        const string username = "admin";
        const string password = "secret";

        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMq(host, port, vhost, username, password, options =>
        {
            capturedOptions = options;
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(host, capturedOptions.Host);
        Assert.Equal(port, capturedOptions.Port);
        Assert.Equal(vhost, capturedOptions.VirtualHost);
        Assert.Equal(username, capturedOptions.UserName);
        Assert.Equal(password, capturedOptions.Password);
    }

    [Fact]
    public void WithRabbitMq_WithFullConfiguration_AllowsAdditionalConfiguration()
    {
        // Arrange
        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMq("localhost", 5672, "/", "guest", "guest", options =>
        {
            capturedOptions = options;
            options.PrefetchCount = 50;
            options.UsePublisherConfirms = false;
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(50, capturedOptions.PrefetchCount);
        Assert.False(capturedOptions.UsePublisherConfirms);
    }

    #endregion

    #region WithRabbitMqSsl Tests

    [Fact]
    public void WithRabbitMqSsl_EnablesSslAndSetsPort()
    {
        // Arrange
        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMqSsl("secure.rabbitmq.com", "admin", "secret", options =>
        {
            capturedOptions = options;
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.UseSsl);
        Assert.Equal(5671, capturedOptions.Port); // Default SSL port
        Assert.Equal("secure.rabbitmq.com", capturedOptions.Host);
        Assert.Equal("admin", capturedOptions.UserName);
        Assert.Equal("secret", capturedOptions.Password);
    }

    [Fact]
    public void WithRabbitMqSsl_AllowsCustomConfiguration()
    {
        // Arrange
        RabbitMqTransportOptions? capturedOptions = null;

        // Act
        _mockBuilder.Object.WithRabbitMqSsl("localhost", "user", "pass", options =>
        {
            capturedOptions = options;
            options.Port = 5672; // Override default SSL port
            options.VirtualHost = "/custom";
        });

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions.UseSsl);
        Assert.Equal(5672, capturedOptions.Port); // Custom port
        Assert.Equal("/custom", capturedOptions.VirtualHost);
    }

    #endregion

    #region WithRabbitMqTopology Tests

    [Fact]
    public void WithRabbitMqTopology_RegistersTopology()
    {
        // Act
        var result = _mockBuilder.Object.WithRabbitMqTopology(topology =>
        {
            topology.Exchange("test-exchange", ExchangeType.Topic);
            topology.Queue("test-queue");
            topology.Bind("test-exchange", "test-queue", "test.#");
        });

        // Assert
        Assert.NotNull(result);
        var serviceDescriptor = _services.FirstOrDefault(sd => sd.ServiceType == typeof(TransportTopology));
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(ServiceLifetime.Singleton, serviceDescriptor.Lifetime);
    }

    [Fact]
    public void WithRabbitMqTopology_CreatesTopologyWithCorrectElements()
    {
        // Arrange
        TransportTopology? capturedTopology = null;

        // Act
        _mockBuilder.Object.WithRabbitMqTopology(topology =>
        {
            topology
                .Exchange("orders", ExchangeType.Topic, ex =>
                {
                    ex.Durable = true;
                    ex.AutoDelete = false;
                })
                .Queue("order-processing", q =>
                {
                    q.Durable = true;
                    q.MaxLength = 10000;
                })
                .Queue("order-failed", q =>
                {
                    q.Durable = true;
                })
                .Bind("orders", "order-processing", "order.created");
        });

        // Get the registered topology from services
        var provider = _services.BuildServiceProvider();
        capturedTopology = provider.GetService<TransportTopology>();

        // Assert
        Assert.NotNull(capturedTopology);
        Assert.Single(capturedTopology.Exchanges);
        Assert.Equal(2, capturedTopology.Queues.Count);
        Assert.Single(capturedTopology.Bindings);

        var exchange = capturedTopology.Exchanges.First();
        Assert.Equal("orders", exchange.Name);
        Assert.Equal(ExchangeType.Topic, exchange.Type);
        Assert.True(exchange.Durable);

        var queue = capturedTopology.Queues.First(q => q.Name == "order-processing");
        Assert.Equal(10000, queue.MaxLength);

        var binding = capturedTopology.Bindings.First();
        Assert.Equal("orders", binding.SourceExchange);
        Assert.Equal("order-processing", binding.Destination);
        Assert.Equal("order.created", binding.RoutingKey);
    }

    [Fact]
    public void WithRabbitMqTopology_EmptyConfiguration_RegistersEmptyTopology()
    {
        // Act
        _mockBuilder.Object.WithRabbitMqTopology(topology =>
        {
            // Empty configuration
        });

        // Get the registered topology
        var provider = _services.BuildServiceProvider();
        var capturedTopology = provider.GetService<TransportTopology>();

        // Assert
        Assert.NotNull(capturedTopology);
        Assert.Empty(capturedTopology.Exchanges);
        Assert.Empty(capturedTopology.Queues);
        Assert.Empty(capturedTopology.Bindings);
    }

    #endregion

    #region Integration with IServiceCollection Tests

    [Fact]
    public void WithRabbitMq_RegistersFactoryThatCreatesTransport()
    {
        // Arrange
        _services.AddLogging();

        // Act
        _mockBuilder.Object.WithRabbitMq("localhost");

        // Assert
        var provider = _services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();

        Assert.NotNull(transport);
        Assert.IsType<HeroMessaging.Transport.RabbitMQ.RabbitMqTransport>(transport);
    }

    [Fact]
    public void WithRabbitMq_RegisteredTransport_HasCorrectName()
    {
        // Arrange
        _services.AddLogging();

        // Act
        _mockBuilder.Object.WithRabbitMq("testhost");

        // Assert
        var provider = _services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();

        Assert.NotNull(transport);
        Assert.Equal("RabbitMQ", transport.Name);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void WithRabbitMq_ReturnsBuilder_AllowsChaining()
    {
        // Act
        var result = _mockBuilder.Object
            .WithRabbitMq("localhost")
            .WithRabbitMqTopology(t => t.Queue("test"));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, _services.Count); // Transport + Topology
    }

    #endregion
}
