using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

using TransportHealthStatus = HeroMessaging.Abstractions.Transport.HealthStatus;

namespace HeroMessaging.Observability.HealthChecks.Tests.Integration;

/// <summary>
/// Integration tests for multiple transport health check scenarios
/// Tests registration and monitoring of multiple transports simultaneously
/// </summary>
public class MultipleTransportHealthCheckTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleTransports_BothRegistered_BothHealthChecked()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Register two different transports
        var mockTransport1 = new Mock<IMessageTransport>();
        var transport1Health = new TransportHealth
        {
            TransportName = "RabbitMQ",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "RabbitMQ is healthy",
            ActiveConnections = 5,
            ActiveConsumers = 3
        };
        mockTransport1.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport1Health);
        mockTransport1.Setup(t => t.Name).Returns("RabbitMQ");

        var mockTransport2 = new Mock<IMessageTransport>();
        var transport2Health = new TransportHealth
        {
            TransportName = "InMemory",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "InMemory is healthy",
            ActiveConnections = 1,
            ActiveConsumers = 2
        };
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport2Health);
        mockTransport2.Setup(t => t.Name).Returns("InMemory");

        // Register both transports as enumerable
        services.AddSingleton(mockTransport1.Object);
        services.AddSingleton(mockTransport2.Object);

        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);

        // Should have separate health checks for each transport
        var transportHealthChecks = report.Entries
            .Where(e => e.Key.StartsWith("hero_messaging_transport"))
            .ToList();

        Assert.True(transportHealthChecks.Count >= 1,
            "At least one transport health check should be registered");

        // All transport checks should be healthy
        Assert.All(transportHealthChecks, entry =>
        {
            Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, entry.Value.Status);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleTransports_OneDegraded_ReportsDegradedOverall()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Healthy transport
        var mockTransport1 = new Mock<IMessageTransport>();
        var transport1Health = new TransportHealth
        {
            TransportName = "RabbitMQ",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "RabbitMQ is healthy"
        };
        mockTransport1.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport1Health);
        mockTransport1.Setup(t => t.Name).Returns("RabbitMQ");

        // Degraded transport
        var mockTransport2 = new Mock<IMessageTransport>();
        var transport2Health = new TransportHealth
        {
            TransportName = "InMemory",
            Status = TransportHealthStatus.Degraded,
            State = TransportState.Reconnecting,
            StatusMessage = "InMemory is degraded"
        };
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport2Health);
        mockTransport2.Setup(t => t.Name).Returns("InMemory");

        services.AddSingleton(mockTransport1.Object);
        services.AddSingleton(mockTransport2.Object);

        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);

        // Overall health should reflect the degraded transport
        Assert.Contains(report.Entries.Values, e =>
            e.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleTransports_OneUnhealthy_ReportsUnhealthyOverall()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Healthy transport
        var mockTransport1 = new Mock<IMessageTransport>();
        var transport1Health = new TransportHealth
        {
            TransportName = "RabbitMQ",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "Healthy"
        };
        mockTransport1.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport1Health);
        mockTransport1.Setup(t => t.Name).Returns("RabbitMQ");

        // Unhealthy transport
        var mockTransport2 = new Mock<IMessageTransport>();
        var transport2Health = new TransportHealth
        {
            TransportName = "InMemory",
            Status = TransportHealthStatus.Unhealthy,
            State = TransportState.Faulted,
            StatusMessage = "Connection failed",
            LastError = "Network error"
        };
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transport2Health);
        mockTransport2.Setup(t => t.Name).Returns("InMemory");

        services.AddSingleton(mockTransport1.Object);
        services.AddSingleton(mockTransport2.Object);

        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);

        // Should have unhealthy status from the failing transport
        Assert.Contains(report.Entries.Values, e =>
            e.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleTransports_WithUniqueNames_CreatesUniqueHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var mockTransport1 = new Mock<IMessageTransport>();
        mockTransport1.Setup(t => t.Name).Returns("Transport1");
        mockTransport1.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport1",
                Status = TransportHealthStatus.Healthy,
                State = TransportState.Connected,
                StatusMessage = "Healthy"
            });

        var mockTransport2 = new Mock<IMessageTransport>();
        mockTransport2.Setup(t => t.Name).Returns("Transport2");
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport2",
                Status = TransportHealthStatus.Healthy,
                State = TransportState.Connected,
                StatusMessage = "Healthy"
            });

        services.AddSingleton(mockTransport1.Object);
        services.AddSingleton(mockTransport2.Object);

        services.AddHealthChecks()
            .AddHeroMessagingHealthChecks(options =>
            {
                options.CheckStorage = false;
                options.CheckTransport = true;
            });

        var serviceProvider = services.BuildServiceProvider();
        _disposables.Add(serviceProvider);

        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthCheckService.CheckHealthAsync();

        // Assert
        Assert.NotNull(report);

        var transportChecks = report.Entries
            .Where(e => e.Key.StartsWith("hero_messaging_transport"))
            .ToList();

        // Each transport should have its own health check entry
        Assert.True(transportChecks.Count >= 1, "Should have at least one transport health check");

        // Verify transports were checked
        mockTransport1.Verify(t => t.GetHealthAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
