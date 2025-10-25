namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Manages connections to message transport
/// Handles connection pooling, reconnection, and lifecycle
/// </summary>
public interface IConnectionManager : IAsyncDisposable
{
    /// <summary>
    /// Gets the transport name
    /// </summary>
    string TransportName { get; }

    /// <summary>
    /// Gets the current connection state
    /// </summary>
    TransportState State { get; }

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

    /// <summary>
    /// Event raised when connection state changes
    /// </summary>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when a connection error occurs
    /// </summary>
    event EventHandler<TransportErrorEventArgs>? Error;
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
public class ConnectionMetrics
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
    /// Number of failed connection attempts
    /// </summary>
    public long FailedConnectionAttempts { get; set; }

    /// <summary>
    /// Number of successful connection attempts
    /// </summary>
    public long SuccessfulConnectionAttempts { get; set; }

    /// <summary>
    /// Average connection establishment time
    /// </summary>
    public TimeSpan AverageConnectionTime { get; set; }

    /// <summary>
    /// Last connection error
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Last connection error time
    /// </summary>
    public DateTime? LastErrorTime { get; set; }

    /// <summary>
    /// Connection success rate (0.0 - 1.0)
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = SuccessfulConnectionAttempts + FailedConnectionAttempts;
            return total > 0 ? (double)SuccessfulConnectionAttempts / total : 0.0;
        }
    }
}
