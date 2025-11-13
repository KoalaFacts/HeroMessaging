using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

using TransportHealthStatus = HeroMessaging.Abstractions.Transport.HealthStatus;

namespace HeroMessaging.Observability.HealthChecks.Tests.Unit;

/// <summary>
/// Unit tests for MultipleTransportHealthCheck
/// Testing aggregation of multiple transport health statuses
/// </summary>
public class MultipleTransportHealthCheckTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithNoTransports_ReturnsHealthy()
    {
        // Arrange
        var transports = Array.Empty<IMessageTransport>();
        var healthCheck = new MultipleTransportHealthCheck(transports);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Contains("No transports to check", result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithSingleHealthyTransport_ReturnsHealthy()
    {
        // Arrange
        var mockTransport = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "All good");
        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Contains("1 healthy", result.Description);
        Assert.Contains("0 degraded", result.Description);
        Assert.Contains("0 unhealthy", result.Description);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data["transport_count"]);
        Assert.Equal(1, result.Data["healthy_count"]);
        Assert.Equal(0, result.Data["degraded_count"]);
        Assert.Equal(0, result.Data["unhealthy_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithMultipleHealthyTransports_ReturnsHealthy()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "Good");
        var transport2 = CreateMockTransport("Transport2", TransportHealthStatus.Healthy, "Good");
        var transport3 = CreateMockTransport("Transport3", TransportHealthStatus.Healthy, "Good");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object, transport3.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Contains("3 healthy", result.Description);
        Assert.Equal(3, result.Data["transport_count"]);
        Assert.Equal(3, result.Data["healthy_count"]);
        Assert.Equal(0, result.Data["degraded_count"]);
        Assert.Equal(0, result.Data["unhealthy_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithOneDegradedTransport_ReturnsDegraded()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "Good");
        var transport2 = CreateMockTransport("Transport2", TransportHealthStatus.Degraded, "Slow");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.Contains("1 healthy", result.Description);
        Assert.Contains("1 degraded", result.Description);
        Assert.Contains("0 unhealthy", result.Description);
        Assert.Equal(1, result.Data["healthy_count"]);
        Assert.Equal(1, result.Data["degraded_count"]);
        Assert.Equal(0, result.Data["unhealthy_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithOneUnhealthyTransport_ReturnsUnhealthy()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "Good");
        var transport2 = CreateMockTransport("Transport2", TransportHealthStatus.Unhealthy, "Failed");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("1 healthy", result.Description);
        Assert.Contains("0 degraded", result.Description);
        Assert.Contains("1 unhealthy", result.Description);
        Assert.Equal(1, result.Data["healthy_count"]);
        Assert.Equal(0, result.Data["degraded_count"]);
        Assert.Equal(1, result.Data["unhealthy_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithMixedStatuses_UnhealthyTakesPrecedence()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "Good");
        var transport2 = CreateMockTransport("Transport2", TransportHealthStatus.Degraded, "Slow");
        var transport3 = CreateMockTransport("Transport3", TransportHealthStatus.Unhealthy, "Failed");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object, transport3.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("1 healthy", result.Description);
        Assert.Contains("1 degraded", result.Description);
        Assert.Contains("1 unhealthy", result.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithTransportException_ReturnsUnhealthyWithException()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "Good");
        var mockTransport2 = new Mock<IMessageTransport>();
        var expectedException = new InvalidOperationException("Transport failed");
        mockTransport2.Setup(t => t.Name).Returns("Transport2");
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, mockTransport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.Contains("1 healthy", result.Description);
        Assert.Contains("1 unhealthy", result.Description);
        Assert.Equal(expectedException, result.Exception);
        Assert.NotNull(result.Data);
        Assert.Contains("transport_1_description", result.Data.Keys);
        Assert.Contains("Health check failed", result.Data["transport_1_description"] as string);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithNullStatusMessage_UsesDefaultMessage()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        mockTransport.Setup(t => t.Name).Returns("Transport1");
        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport1",
                Status = TransportHealthStatus.Healthy,
                StatusMessage = null,
                State = TransportState.Connected
            });

        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.Contains("transport_0_description", result.Data.Keys);
        var description = result.Data["transport_0_description"] as string;
        Assert.Contains("Transport1", description);
        Assert.Contains("is healthy", description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithEmptyStatusMessage_UsesDefaultMessage()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        mockTransport.Setup(t => t.Name).Returns("Transport1");
        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport1",
                Status = TransportHealthStatus.Degraded,
                StatusMessage = "",
                State = TransportState.Reconnecting
            });

        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, result.Status);
        Assert.NotNull(result.Data);
        var description = result.Data["transport_0_description"] as string;
        Assert.Contains("Transport1", description);
        Assert.Contains("is degraded", description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_IncludesTransportStatusesInData()
    {
        // Arrange
        var transport1 = CreateMockTransport("RabbitMQ", TransportHealthStatus.Healthy, "Good");
        var transport2 = CreateMockTransport("InMemory", TransportHealthStatus.Degraded, "Slow");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.NotNull(result.Data);
        Assert.Contains("transport_statuses", result.Data.Keys);
        var statuses = result.Data["transport_statuses"] as List<string>;
        Assert.NotNull(statuses);
        Assert.Contains("RabbitMQ=Healthy", statuses);
        Assert.Contains("InMemory=Degraded", statuses);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_IncludesIndividualTransportDetails()
    {
        // Arrange
        var transport1 = CreateMockTransport("Transport1", TransportHealthStatus.Healthy, "All good");
        var transport2 = CreateMockTransport("Transport2", TransportHealthStatus.Degraded, "Slow response");
        var healthCheck = new MultipleTransportHealthCheck(new[] { transport1.Object, transport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal("Transport1", result.Data["transport_0_name"]);
        Assert.Equal("Healthy", result.Data["transport_0_status"]);
        Assert.Contains("All good", result.Data["transport_0_description"] as string);

        Assert.Equal("Transport2", result.Data["transport_1_name"]);
        Assert.Equal("Degraded", result.Data["transport_1_status"]);
        Assert.Contains("Slow response", result.Data["transport_1_description"] as string);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        var cts = new CancellationTokenSource();
        mockTransport.Setup(t => t.Name).Returns("Transport1");
        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport1",
                Status = TransportHealthStatus.Healthy,
                State = TransportState.Connected
            });

        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        mockTransport.Verify(t => t.GetHealthAsync(cts.Token), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_MultipleTransportsCheckedInParallel_CompletesSuccessfully()
    {
        // Arrange
        var transports = new List<Mock<IMessageTransport>>();
        for (int i = 0; i < 10; i++)
        {
            var transport = CreateMockTransport($"Transport{i}", TransportHealthStatus.Healthy, $"Message{i}");
            transports.Add(transport);
        }

        var healthCheck = new MultipleTransportHealthCheck(transports.Select(t => t.Object).ToArray());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result.Status);
        Assert.Equal(10, result.Data["transport_count"]);
        Assert.Equal(10, result.Data["healthy_count"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_WithMultipleExceptions_ReturnsFirstException()
    {
        // Arrange
        var mockTransport1 = new Mock<IMessageTransport>();
        var exception1 = new InvalidOperationException("First error");
        mockTransport1.Setup(t => t.Name).Returns("Transport1");
        mockTransport1.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception1);

        var mockTransport2 = new Mock<IMessageTransport>();
        var exception2 = new InvalidOperationException("Second error");
        mockTransport2.Setup(t => t.Name).Returns("Transport2");
        mockTransport2.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception2);

        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport1.Object, mockTransport2.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        // The exception should be one of the thrown exceptions (parallel execution may vary order)
        Assert.True(result.Exception == exception1 || result.Exception == exception2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTransports_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new MultipleTransportHealthCheck(null!));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckHealthAsync_UnknownHealthStatus_MapsToUnhealthy()
    {
        // Arrange
        var mockTransport = new Mock<IMessageTransport>();
        mockTransport.Setup(t => t.Name).Returns("Transport1");
        mockTransport.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = "Transport1",
                Status = (TransportHealthStatus)999, // Unknown status
                StatusMessage = "Unknown",
                State = TransportState.Unknown
            });

        var healthCheck = new MultipleTransportHealthCheck(new[] { mockTransport.Object });

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
    }

    private Mock<IMessageTransport> CreateMockTransport(string name, TransportHealthStatus status, string message)
    {
        var mock = new Mock<IMessageTransport>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.GetHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportHealth
            {
                TransportName = name,
                Status = status,
                StatusMessage = message,
                State = status == TransportHealthStatus.Healthy ? TransportState.Connected :
                        status == TransportHealthStatus.Degraded ? TransportState.Reconnecting :
                        TransportState.Faulted
            });
        return mock;
    }
}
