namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Manages connections to message transport
/// Handles connection pooling, reconnection, and lifecycle
/// </summary>
public interface IConnectionManager : IAsyncDisposable, ITransportObservability
{
    /// <summary>
    /// Gets the transport name
    /// </summary>
    string TransportName { get; }

    /// <summary>
    /// Gets whether the connection is active
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the transport
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the transport
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconnect to the transport
    /// </summary>
    Task ReconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a connection from the pool
    /// </summary>
    Task<ITransportConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a connection to the pool
    /// </summary>
    Task ReturnConnectionAsync(ITransportConnection connection);

    /// <summary>
    /// Get connection metrics
    /// </summary>
    ConnectionMetrics GetMetrics();
}

/// <summary>
/// Represents a single transport connection
/// </summary>
public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the connection identifier
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets whether the connection is open
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Gets when the connection was established
    /// </summary>
    DateTime EstablishedAt { get; }

    /// <summary>
    /// Get transport-specific connection object
    /// </summary>
    object GetNativeConnection();

    /// <summary>
    /// Close the connection
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Connection pool metrics
/// </summary>
public class ConnectionMetrics : ComponentMetrics
{
    /// <summary>
    /// Total number of connections in the pool
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// Number of active (in-use) connections
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Number of idle connections
    /// </summary>
    public int IdleConnections { get; set; }

    /// <summary>
    /// Number of failed connection attempts (alias for FailedOperations)
    /// </summary>
    public long FailedConnectionAttempts
    {
        get => FailedOperations;
        set => FailedOperations = value;
    }

    /// <summary>
    /// Number of successful connection attempts (alias for SuccessfulOperations)
    /// </summary>
    public long SuccessfulConnectionAttempts
    {
        get => SuccessfulOperations;
        set => SuccessfulOperations = value;
    }

    /// <summary>
    /// Average connection establishment time
    /// </summary>
    public TimeSpan AverageConnectionTime { get; set; }
}
