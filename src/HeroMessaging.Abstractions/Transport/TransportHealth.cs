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
    /// When the health check was performed.
    /// Defaults to system time when not explicitly set.
    /// </summary>
    public DateTime Timestamp { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

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
    public static TransportHealth Healthy(string transportName, TransportState state = TransportState.Connected, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Healthy,
            State = state,
            StatusMessage = "Transport is healthy",
            Timestamp = now,
            Duration = TimeSpan.Zero,
            ActiveConnections = 0,
            ActiveConsumers = 0
        };
    }

    /// <summary>
    /// Create a degraded status
    /// </summary>
    public static TransportHealth Degraded(string transportName, string reason, TransportState state = TransportState.Connected, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Degraded,
            State = state,
            StatusMessage = reason,
            Timestamp = now,
            Duration = TimeSpan.Zero,
            ActiveConnections = 0,
            ActiveConsumers = 0
        };
    }

    /// <summary>
    /// Create an unhealthy status
    /// </summary>
    public static TransportHealth Unhealthy(string transportName, string reason, TransportState state = TransportState.Disconnected, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Unhealthy,
            State = state,
            StatusMessage = reason,
            Timestamp = now,
            Duration = TimeSpan.Zero,
            ActiveConnections = 0,
            ActiveConsumers = 0
        };
    }

    /// <summary>
    /// Create from exception
    /// </summary>
    public static TransportHealth FromException(string transportName, Exception exception, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
        return new TransportHealth
        {
            TransportName = transportName,
            Status = HealthStatus.Unhealthy,
            State = TransportState.Faulted,
            StatusMessage = exception.Message,
            LastError = exception.ToString(),
            LastErrorTime = now,
            Timestamp = now,
            Duration = TimeSpan.Zero,
            ActiveConnections = 0,
            ActiveConsumers = 0
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
