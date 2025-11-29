using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Health check implementation for multiple transports
/// Aggregates health status from all registered transports
/// </summary>
public class MultipleTransportHealthCheck(IEnumerable<IMessageTransport> transports) : IHealthCheck
{
    private readonly List<IMessageTransport> _transports = transports?.ToList() ?? throw new ArgumentNullException(nameof(transports));

    /// <summary>
    /// Check the health of all transports and aggregate results
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_transports.Count == 0)
        {
            return HealthCheckResult.Healthy("No transports to check");
        }

        var results = new List<(string TransportName, Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus Status, string Description, Exception? Exception)>();
        var healthData = new Dictionary<string, object>();

        // Check all transports in parallel
        var tasks = _transports.Select(async transport =>
        {
            try
            {
                var transportHealth = await transport.GetHealthAsync(cancellationToken).ConfigureAwait(false);
                var status = MapHealthStatus(transportHealth.Status);
                var statusMessage = string.IsNullOrWhiteSpace(transportHealth.StatusMessage)
                    ? GetDefaultStatusMessage(status)
                    : transportHealth.StatusMessage;

                return (
                    TransportName: transport.Name,
                    Status: status,
                    Description: $"{transport.Name}: {statusMessage}",
                    Exception: (Exception?)null
                );
            }
            catch (Exception ex)
            {
                return (
                    TransportName: transport.Name,
                    Status: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                    Description: $"{transport.Name}: Health check failed",
                    Exception: ex
                );
            }
        });

        results = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();

        // Aggregate results
        var overallStatus = AggregateStatus(results.Select(r => r.Status));
        var transportStatuses = results.Select(r => $"{r.TransportName}={r.Status}").ToList();

        // Build description
        var unhealthyCount = results.Count(r => r.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
        var degradedCount = results.Count(r => r.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
        var healthyCount = results.Count(r => r.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);

        var description = $"Transports: {healthyCount} healthy, {degradedCount} degraded, {unhealthyCount} unhealthy";

        // Add transport details to data
        healthData["transport_count"] = _transports.Count;
        healthData["healthy_count"] = healthyCount;
        healthData["degraded_count"] = degradedCount;
        healthData["unhealthy_count"] = unhealthyCount;
        healthData["transport_statuses"] = transportStatuses;

        // Add individual transport descriptions
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            healthData[$"transport_{i}_name"] = result.TransportName;
            healthData[$"transport_{i}_status"] = result.Status.ToString();
            healthData[$"transport_{i}_description"] = result.Description;
        }

        // Include first exception if any transport failed
        var firstException = results.FirstOrDefault(r => r.Exception != null).Exception;

        return new HealthCheckResult(overallStatus, description, firstException, healthData);
    }

    /// <summary>
    /// Map TransportHealth.HealthStatus to Microsoft HealthStatus
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus MapHealthStatus(
        Abstractions.Transport.HealthStatus status)
    {
        return status switch
        {
            Abstractions.Transport.HealthStatus.Healthy => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy,
            Abstractions.Transport.HealthStatus.Degraded => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
            Abstractions.Transport.HealthStatus.Unhealthy => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            _ => Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy
        };
    }

    /// <summary>
    /// Aggregate multiple health statuses into a single status
    /// Priority: Unhealthy > Degraded > Healthy
    /// </summary>
    private static Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus AggregateStatus(
        IEnumerable<Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus> statuses)
    {
        var statusList = statuses.ToList();

        if (statusList.Any(s => s == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy))
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;
        }

        if (statusList.Any(s => s == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded))
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded;
        }

        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy;
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
}
