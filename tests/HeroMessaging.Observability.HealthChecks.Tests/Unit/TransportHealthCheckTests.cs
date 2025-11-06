using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

using TransportHealthStatus = HeroMessaging.Abstractions.Transport.HealthStatus;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for TransportHealthCheck class
/// Tests mapping from TransportHealth to ASP.NET Core HealthCheckResult
/// </summary>
public class TransportHealthCheckTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithHealthyTransport_ReturnsHealthy()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "All systems operational",
            ActiveConnections = 5,
            ActiveConsumers = 3,
            PendingMessages = 10,
            Uptime = TimeSpan.FromMinutes(30)
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Contains("TestTransport", result.Description);
        Assert.Contains("operational", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal("TestTransport", result.Data["transport_name"]);
        Assert.Equal("Connected", result.Data["transport_state"]);
        Assert.Equal(5, result.Data["active_connections"]);
        Assert.Equal(3, result.Data["active_consumers"]);
        Assert.Equal(10L, result.Data["pending_messages"]);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Data["uptime"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithDegradedTransport_ReturnsDegraded()
    {
        // Arrange
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

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("TestTransport", result.Description);
        Assert.Contains("degraded", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal("TestTransport", result.Data["transport_name"]);
        Assert.Equal("Reconnecting", result.Data["transport_state"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithUnhealthyTransport_ReturnsUnhealthy()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Unhealthy,
            State = TransportState.Faulted,
            StatusMessage = "Connection failed",
            LastError = "Network error",
            LastErrorTime = DateTime.UtcNow.AddMinutes(-5),
            ActiveConnections = 0,
            ActiveConsumers = 0
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("TestTransport", result.Description);
        Assert.Contains("failed", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal("TestTransport", result.Data["transport_name"]);
        Assert.Equal("Faulted", result.Data["transport_state"]);
        Assert.Equal("Network error", result.Data["last_error"]);
        Assert.NotNull(result.Data["last_error_time"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WhenTransportThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var exception = new InvalidOperationException("Transport unavailable");

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("TestTransport", result.Description);
        Assert.Contains("check failed", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal("TestTransport", result.Data["transport_name"]);
        Assert.Equal("Transport unavailable", result.Data["error"]);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithCustomData_IncludesCustomDataInResult()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var customData = new Dictionary<string, object>
        {
            ["custom_metric_1"] = 42,
            ["custom_metric_2"] = "custom_value"
        };

        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = "Healthy",
            Data = customData
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal(42, result.Data["custom_metric_1"]);
        Assert.Equal("custom_value", result.Data["custom_metric_2"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTransport_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TransportHealthCheck(null!));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithTimeout_PropagatesCancellation()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, cts.Token);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("check failed", result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithNullStatusMessage_UsesDefaultMessage()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Healthy,
            State = TransportState.Connected,
            StatusMessage = null,  // Null status message
            ActiveConnections = 1,
            ActiveConsumers = 0
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Description);
        Assert.Contains("TestTransport", result.Description);
        Assert.DoesNotContain(":", result.Description.Trim().TrimEnd());  // Should not end with just ":"
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithEmptyStatusMessage_UsesDefaultMessage()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var transportHealth = new TransportHealth
        {
            TransportName = "TestTransport",
            Status = TransportHealthStatus.Degraded,
            State = TransportState.Reconnecting,
            StatusMessage = "",  // Empty status message
            ActiveConnections = 0,
            ActiveConsumers = 0
        };

        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transportHealth);
        mockTransport.Setup(t => t.Name).Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(mockTransport.Object);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.NotNull(result.Description);
        Assert.Contains("TestTransport", result.Description);
        Assert.DoesNotContain(": ", result.Description.TrimEnd());  // Should not have hanging ": "
    }
}
