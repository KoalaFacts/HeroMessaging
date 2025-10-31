using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Health check implementation for IMessageTransport that adapts the transport's native health check to the ASP.NET Core health check interface.
/// </summary>
/// <remarks>
/// This health check wraps a message transport's GetHealthAsync method and converts the result
/// to the standard ASP.NET Core HealthCheckResult format. It includes detailed diagnostic data
/// such as connection counts, consumer counts, pending messages, and transport state.
///
/// The check will report:
/// - Healthy: Transport is operating normally
/// - Degraded: Transport is operational but experiencing issues
/// - Unhealthy: Transport is not operational or threw an exception
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("my-transport", new TransportHealthCheck(transport, "MyTransport"));
/// </code>
/// </remarks>
public class TransportHealthCheck(IMessageTransport transport, string? name = null) : IHealthCheck
{
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly string _name = name ?? transport?.Name ?? "transport";

    /// <summary>
    /// Checks the health of the message transport by calling its GetHealthAsync method.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result with status, description, and diagnostic data</returns>
    /// <remarks>
    /// This method calls the transport's GetHealthAsync method and maps the result to ASP.NET Core's
    /// HealthCheckResult format. If an exception occurs, the check returns Unhealthy with error details.
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var transportHealth = await _transport.GetHealthAsync(cancellationToken);
            return MapToHealthCheckResult(transportHealth);
        }
        catch (OperationCanceledException ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed - operation cancelled",
                ex,
                CreateErrorData(ex));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Health check failed",
                ex,
                CreateErrorData(ex));
        }
    }

    /// <summary>
    /// Maps a TransportHealth object to an ASP.NET Core HealthCheckResult.
    /// </summary>
    /// <param name="transportHealth">The transport health information to map</param>
    /// <returns>A HealthCheckResult with mapped status, description, and diagnostic data</returns>
    private HealthCheckResult MapToHealthCheckResult(TransportHealth transportHealth)
    {
        var status = transportHealth.Status switch
        {
            Abstractions.Transport.HealthStatus.Healthy => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy,
            Abstractions.Transport.HealthStatus.Degraded => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
            Abstractions.Transport.HealthStatus.Unhealthy => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            _ => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy
        };

        var data = CreateHealthData(transportHealth);

        // Handle null or empty status message
        // When status message is provided, use "Name: Message" format
        // When status message is null/empty, use "Name DefaultMessage" format (no colon)
        string description;
        if (string.IsNullOrWhiteSpace(transportHealth.StatusMessage))
        {
            var defaultMessage = GetDefaultStatusMessage(status);
            description = $"{transportHealth.TransportName} {defaultMessage}";
        }
        else
        {
            description = $"{transportHealth.TransportName}: {transportHealth.StatusMessage}";
        }

        return new HealthCheckResult(status, description, null, data);
    }

    /// <summary>
    /// Gets a default human-readable status message based on the health status.
    /// </summary>
    /// <param name="status">The health status to get a message for</param>
    /// <returns>A descriptive message for the given health status</returns>
    private static string GetDefaultStatusMessage(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus status)
    {
        return status switch
        {
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => "Transport is healthy",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => "Transport is degraded",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy => "Transport is unhealthy",
            _ => "Unknown status"
        };
    }

    /// <summary>
    /// Creates a dictionary of diagnostic data from the TransportHealth object.
    /// </summary>
    /// <param name="health">The transport health information to extract data from</param>
    /// <returns>A dictionary containing transport diagnostic data including name, state, connections, and optional metrics</returns>
    private static Dictionary<string, object> CreateHealthData(TransportHealth health)
    {
        var data = new Dictionary<string, object>
        {
            ["transport_name"] = health.TransportName,
            ["transport_state"] = health.State.ToString(),
            ["active_connections"] = health.ActiveConnections,
            ["active_consumers"] = health.ActiveConsumers,
            ["timestamp"] = health.Timestamp
        };

        if (health.PendingMessages.HasValue)
        {
            data["pending_messages"] = health.PendingMessages.Value;
        }

        if (health.Uptime.HasValue)
        {
            data["uptime"] = health.Uptime.Value;
        }

        if (!string.IsNullOrEmpty(health.LastError))
        {
            data["last_error"] = health.LastError;
        }

        if (health.LastErrorTime.HasValue)
        {
            data["last_error_time"] = health.LastErrorTime.Value;
        }

        if (health.Duration != TimeSpan.Zero)
        {
            data["health_check_duration"] = health.Duration;
        }

        // Include custom data from transport
        if (health.Data != null)
        {
            foreach (var kvp in health.Data)
            {
                data[kvp.Key] = kvp.Value;
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a dictionary of error diagnostic data from an exception.
    /// </summary>
    /// <param name="ex">The exception that occurred during the health check</param>
    /// <returns>A dictionary containing transport name, error message, and error type</returns>
    private Dictionary<string, object> CreateErrorData(Exception ex)
    {
        return new Dictionary<string, object>
        {
            ["transport_name"] = _name,
            ["error"] = ex.Message,
            ["error_type"] = ex.GetType().Name
        };
    }
}
