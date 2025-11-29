using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Health check implementation for IMessageTransport
/// Wraps the transport's native health check and adapts it to ASP.NET Core health check interface
/// </summary>
public class TransportHealthCheck(IMessageTransport transport, string? name = null) : IHealthCheck
{
    private readonly IMessageTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly string _name = name ?? transport?.Name ?? "transport";

    /// <summary>
    /// Check the health of the message transport
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var transportHealth = await _transport.GetHealthAsync(cancellationToken).ConfigureAwait(false);
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
    /// Map TransportHealth to HealthCheckResult
    /// </summary>
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
    /// Get default status message based on health status
    /// </summary>
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
    /// Create health data dictionary from TransportHealth
    /// </summary>
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
    /// Create error data dictionary from exception
    /// </summary>
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
