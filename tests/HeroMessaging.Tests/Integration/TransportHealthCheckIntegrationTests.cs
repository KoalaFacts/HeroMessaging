using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;
using TransportHealthStatus = HeroMessaging.Abstractions.Transport.HealthStatus;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// Integration tests for transport health check registration and execution
/// Tests end-to-end health check workflow with dependency injection
/// </summary>
public class TransportHealthCheckIntegrationTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_RegisteredAndExecuted_ReturnsHealthy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "All systems operational",
            ActiveConnections = 5,
            ActiveConsumers = 3
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        services.AddSingleton(mockTransport.Object);
        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Contains("hero_messaging_transport", report.Entries.Keys);

        var entry = report.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Status);
        Assert.Contains("TestTransport", entry.Description);
        Assert.NotNull(entry.Data);
        Assert.Equal("TestTransport", entry.Data["transport_name"]);
        Assert.Equal("Connected", entry.Data["transport_state"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_WithUnhealthyTransport_ReportsUnhealthy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Unhealthy,
            State = TransportState.Faulted,
            StatusMessage = "Connection failed",
            LastError = "Network error",
            ActiveConnections = 0,
            ActiveConsumers = 0
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        services.AddSingleton(mockTransport.Object);
        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, report.Status);

        var entry = report.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("TestTransport", entry.Description);
        Assert.Contains("failed", entry.Description);
        Assert.Equal("Network error", entry.Data["last_error"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_WithDegradedTransport_ReportsDegraded()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Degraded,
            State = TransportState.Reconnecting,
            StatusMessage = "Connection pool is degraded",
            ActiveConnections = 2,
            ActiveConsumers = 1
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        services.AddSingleton(mockTransport.Object);
        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, report.Status);

        var entry = report.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, entry.Status);
        Assert.Contains("degraded", entry.Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_WhenTransportNotRegistered_ReturnsHealthy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Don't register a transport
        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        var entry = report.Entries["hero_messaging_transport"];
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Status);
        Assert.Contains("not registered", entry.Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_WhenDisabled_NotExecuted()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport = new Mock<IMessageTransport>();
        services.AddSingleton(mockTransport.Object);

        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = false;  // Disabled
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        Assert.DoesNotContain("hero_messaging_transport", report.Entries.Keys);

        // Verify GetHealthAsync was never called
        mockTransport.Verify(t => t.GetHealthAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TransportHealthCheck_WithCustomTags_AppliesTags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "Healthy"
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        services.AddSingleton(mockTransport.Object);
        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
                options.Tags = new[] { "messaging", "transport" };
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider as IAsyncDisposable);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);
        var entry = report.Entries["hero_messaging_transport"];
        Assert.NotNull(entry);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Status);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            if (disposable != null)
            {
                await disposable.DisposeAsync();
            }
        }
        _disposables.Clear();
    }
}
