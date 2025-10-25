namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Health information for a message transport
/// </summary>
public class TransportHealth
{
    /// <summary>
    /// Overall health status
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// Transport name
    /// </summary>
    public required string TransportName { get; set; }

    /// <summary>
    /// Current transport state
    /// </summary>
    public TransportState State { get; set; }

    /// <summary>
    /// Connection status message
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// When the health check was performed
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the health check
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of active connections
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Number of active consumers
    /// </summary>
    public int ActiveConsumers { get; set; }

    /// <summary>
    /// Number of messages pending
    /// </summary>
    public long? PendingMessages { get; set; }

    /// <summary>
    /// Connection uptime
    /// </summary>
    public TimeSpan? Uptime { get; set; }

    /// <summary>
    /// Last error that occurred
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When the last error occurred
    /// </summary>
    public DateTime? LastErrorTime { get; set; }

    /// <summary>
    /// Additional health data
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Create a healthy status
    /// </summary>
    public static TransportHealth Healthy(string transportName, TransportState state = TransportState.Connected)
    {
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Healthy,
            State = state,
            StatusMessage = "Transport is healthy"
        };
    }

    /// <summary>
    /// Create a degraded status
    /// </summary>
    public static TransportHealth Degraded(string transportName, string reason, TransportState state = TransportState.Connected)
    {
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Degraded,
            State = state,
            StatusMessage = reason
        };
    }

    /// <summary>
    /// Create an unhealthy status
    /// </summary>
    public static TransportHealth Unhealthy(string transportName, string reason, TransportState state = TransportState.Disconnected)
    {
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Unhealthy,
            State = state,
            StatusMessage = reason
        };
    }

    /// <summary>
    /// Create from exception
    /// </summary>
    public static TransportHealth FromException(string transportName, Exception exception)
    {
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Unhealthy,
            State = TransportState.Faulted,
            StatusMessage = exception.Message,
            LastError = exception.ToString(),
            LastErrorTime = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Health status enumeration
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Transport is healthy and functioning normally
    /// </summary>
    Healthy,

    /// <summary>
    /// Transport is functioning but with degraded performance or warnings
    /// </summary>
    Degraded,

    /// <summary>
    /// Transport is not functioning properly
    /// </summary>
    Unhealthy
}
