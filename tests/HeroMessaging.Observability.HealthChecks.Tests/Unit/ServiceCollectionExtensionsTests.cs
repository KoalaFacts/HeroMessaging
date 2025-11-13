using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

using TransportHealthStatus = HeroMessaging.Abstractions.Transport.HealthStatus;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for ServiceCollectionExtensions transport-related functionality
/// Testing transport health check registration scenarios
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithTransportEnabled_RegistersTransportHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var mockTransport = CreateMockTransport("TestTransport", TransportHealthStatus.Healthy);
        services.AddSingleton(mockTransport.Object);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
            options.CheckTransport = true;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_transport");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Entries["hero_messaging_transport"].Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithTransportDisabled_DoesNotRegisterTransportCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
            options.CheckTransport = false;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.DoesNotContain(result.Entries, e => e.Key == "hero_messaging_transport");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithNoTransportRegistered_UsesAlwaysHealthyCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // Note: Not registering any transport implementation

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
            options.CheckTransport = true;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_transport");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Entries["hero_messaging_transport"].Status);
        Assert.Contains("not registered", result.Entries["hero_messaging_transport"].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithSingleTransport_UsesTransportHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var mockTransport = CreateMockTransport("RabbitMQ", TransportHealthStatus.Healthy, "Connected");
        services.AddSingleton(mockTransport.Object);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
            options.CheckTransport = true;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_transport");
        var entry = result.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Status);
        Assert.Contains("RabbitMQ", entry.Description);
        Assert.Contains("Connected", entry.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithMultipleTransports_UsesMultipleTransportHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var mockTransport1 = CreateMockTransport("RabbitMQ", TransportHealthStatus.Healthy);
        var mockTransport2 = CreateMockTransport("InMemory", TransportHealthStatus.Healthy);
        services.AddSingleton(mockTransport1.Object);
        services.AddSingleton(mockTransport2.Object);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = false;
            options.CheckTransport = true;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_transport");
        var entry = result.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Status);
        Assert.Contains("2 healthy", entry.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_WithTransportAndStorage_RegistersBoth()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Mock.Of<HeroMessaging.Abstractions.Storage.IMessageStorage>());

        var mockTransport = CreateMockTransport("TestTransport", TransportHealthStatus.Healthy);
        services.AddSingleton(mockTransport.Object);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(options =>
        {
            options.CheckStorage = true;
            options.CheckOutboxStorage = false;
            options.CheckInboxStorage = false;
            options.CheckQueueStorage = false;
            options.CheckTransport = true;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_message_storage");
        Assert.Contains(result.Entries, e => e.Key == "hero_messaging_transport");
        Assert.True(result.Entries.Count >= 2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingHealthCheckOptions_DefaultTransportValue_IsFalse()
    {
        // Arrange & Act
        var options = new HeroMessagingHealthCheckOptions();

        // Assert
        Assert.False(options.CheckTransport);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingHealthCheckOptions_CanEnableTransportCheck()
    {
        // Arrange
        var options = new HeroMessagingHealthCheckOptions();

        // Act
        options.CheckTransport = true;

        // Assert
        Assert.True(options.CheckTransport);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddHeroMessagingHealthChecks_NullConfigure_UsesDefaultOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var healthChecksBuilder = services.AddHealthChecks();

        // Act
        healthChecksBuilder.AddHeroMessagingHealthChecks(configure: null);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.NotNull(result);
        // Default options should register storage checks but not transport
        Assert.DoesNotContain(result.Entries, e => e.Key == "hero_messaging_transport");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddCompositeHealthCheck_WithEmptyCheckNames_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var healthChecksBuilder = services.AddHealthChecks();
        var checkNames = Array.Empty<string>();

        // Act
        healthChecksBuilder.AddCompositeHealthCheck("empty_composite", checkNames);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        Assert.Contains(result.Entries, e => e.Key == "empty_composite");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Entries["empty_composite"].Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddCompositeHealthCheck_IncludesCheckCountInData()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var healthChecksBuilder = services.AddHealthChecks();
        var checkNames = new[] { "check1", "check2", "check3" };

        // Act
        healthChecksBuilder.AddCompositeHealthCheck("data_composite", checkNames);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        var result = await healthCheckService.CheckHealthAsync();

        var entry = result.Entries["data_composite"];
        Assert.NotNull(entry.Data);
        Assert.True(entry.Data.ContainsKey("check_count"));
        Assert.Equal(3, entry.Data["check_count"]);
    }

    private Mock<IMessageTransport> CreateMockTransport(string name, TransportHealthStatus status, string statusMessage = "OK")
    {
        var mock = new Mock<IMessageTransport>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = name,
                Status = status,
                StatusMessage = statusMessage,
                State = status == TransportHealthStatus.Healthy ? TransportState.Connected :
                        status == TransportHealthStatus.Degraded ? TransportState.Reconnecting :
                        TransportState.Faulted,
                ActiveConnections = 1,
                ActiveConsumers = 0
            });
        return mock;
    }
}
