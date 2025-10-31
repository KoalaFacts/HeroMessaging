using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Health check implementation for multiple message transports that aggregates health status from all registered transports.
/// </summary>
/// <remarks>
/// This health check runs health checks on multiple transports in parallel and aggregates the results
/// into a single health status. The aggregation follows these rules:
/// - If any transport is Unhealthy, the overall status is Unhealthy
/// - If any transport is Degraded (and none are Unhealthy), the overall status is Degraded
/// - If all transports are Healthy, the overall status is Healthy
///
/// The health check result includes:
/// - Count of healthy, degraded, and unhealthy transports
/// - Individual status for each transport
/// - Detailed diagnostic data for each transport
/// - First exception encountered (if any)
///
/// Example usage:
/// <code>
/// var transports = serviceProvider.GetServices&lt;IMessageTransport&gt;();
/// services.AddHealthChecks()
///     .AddCheck("all-transports", new MultipleTransportHealthCheck(transports));
/// </code>
/// </remarks>
public class MultipleTransportHealthCheck(IEnumerable<IMessageTransport> transports) : IHealthCheck
{
    private readonly List<IMessageTransport> _transports = transports?.ToList() ?? throw new ArgumentNullException(nameof(transports));

    /// <summary>
    /// Checks the health of all registered transports in parallel and aggregates the results.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the aggregated health check result with counts, statuses, and diagnostic data</returns>
    /// <remarks>
    /// This method executes health checks for all transports concurrently for performance.
    /// If any transport throws an exception, it is recorded as Unhealthy but does not prevent
    /// other transports from being checked.
    /// </remarks>
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
                var transportHealth = await transport.GetHealthAsync(cancellationToken);
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

        results = (await Task.WhenAll(tasks)).ToList();

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
    /// Maps a HeroMessaging transport health status to an ASP.NET Core health status.
    /// </summary>
    /// <param name="status">The HeroMessaging health status to map</param>
    /// <returns>The equivalent ASP.NET Core health status</returns>
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
    /// Aggregates multiple health statuses into a single overall status using priority rules.
    /// </summary>
    /// <param name="statuses">The collection of health statuses to aggregate</param>
    /// <returns>The overall health status based on priority: Unhealthy &gt; Degraded &gt; Healthy</returns>
    /// <remarks>
    /// If any status is Unhealthy, returns Unhealthy.
    /// If any status is Degraded (and none are Unhealthy), returns Degraded.
    /// Otherwise returns Healthy.
    /// </remarks>
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
}
