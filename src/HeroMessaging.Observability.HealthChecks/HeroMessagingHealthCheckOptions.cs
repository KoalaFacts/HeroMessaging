using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Configuration options for HeroMessaging health checks
/// </summary>
public class HeroMessagingHealthCheckOptions
{
    /// <summary>
    /// Enable storage health checks
    /// </summary>
    public bool CheckStorage { get; set; } = true;

    /// <summary>
    /// Enable message storage health check
    /// </summary>
    public bool CheckMessageStorage { get; set; } = true;

    /// <summary>
    /// Enable outbox storage health check
    /// </summary>
    public bool CheckOutboxStorage { get; set; } = true;

    /// <summary>
    /// Enable inbox storage health check
    /// </summary>
    public bool CheckInboxStorage { get; set; } = true;

    /// <summary>
    /// Enable queue storage health check
    /// </summary>
    public bool CheckQueueStorage { get; set; } = true;

    /// <summary>
    /// Enable transport health check
    /// </summary>
    public bool CheckTransport { get; set; } = false;

    /// <summary>
    /// Health status to return when health check fails
    /// </summary>
    public HealthStatus? FailureStatus { get; set; } = HealthStatus.Unhealthy;

    /// <summary>
    /// Tags to apply to health check registrations
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; set; }
}
