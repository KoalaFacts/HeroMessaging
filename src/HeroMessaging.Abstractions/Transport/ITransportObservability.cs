namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Provides event notifications for transport state changes and errors.
/// Implements the observable pattern for monitoring transport lifecycle and failures.
/// </summary>
/// <remarks>
/// This interface is implemented by both <see cref="IMessageTransport"/> and <see cref="IConnectionManager"/>
/// to provide a consistent observability model across transport components.
///
/// Use this interface to:
/// - Monitor connection state transitions (connecting, connected, disconnecting, etc.)
/// - Track reconnection attempts and failures
/// - Implement custom error handling and alerting
/// - Build health monitoring dashboards
///
/// Example usage:
/// <code>
/// transport.StateChanged += (sender, e) =>
/// {
///     logger.LogInformation(
///         "Transport state changed from {PreviousState} to {CurrentState}. Reason: {Reason}",
///         e.PreviousState, e.CurrentState, e.Reason);
/// };
///
/// transport.Error += (sender, e) =>
/// {
///     logger.LogError(e.Exception,
///         "Transport error occurred. Context: {Context}", e.Context);
///
///     // Implement custom alerting
///     await alertingService.SendAlertAsync(e.Exception, e.Context);
/// };
/// </code>
/// </remarks>
public interface ITransportObservability
{
    /// <summary>
    /// Gets the current connection state of the transport.
    /// </summary>
    /// <value>
    /// The current <see cref="TransportState"/> value representing the connection status.
    /// </value>
    /// <remarks>
    /// This property is thread-safe and can be safely accessed from multiple threads.
    /// Subscribe to <see cref="StateChanged"/> to be notified when the state changes.
    /// </remarks>
    TransportState State { get; }

    /// <summary>
    /// Event raised when the transport state changes.
    /// </summary>
    /// <remarks>
    /// This event is raised on state transitions such as:
    /// - Disconnected to Connecting
    /// - Connecting to Connected
    /// - Connected to Reconnecting
    /// - Any state to Faulted
    ///
    /// Event handlers execute synchronously on the transport's internal thread.
    /// Avoid long-running operations in event handlers to prevent blocking state transitions.
    /// Use async event handlers or queue work to background tasks if needed.
    ///
    /// The event provides both the previous and current state, along with an optional
    /// reason for the transition.
    /// </remarks>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when an error occurs in the transport.
    /// </summary>
    /// <remarks>
    /// This event is raised for both recoverable and non-recoverable errors:
    /// - Connection failures
    /// - Send/receive timeouts
    /// - Serialization errors
    /// - Authentication failures
    /// - Network interruptions
    ///
    /// The event provides the exception and optional context information about
    /// where the error occurred (e.g., "SendAsync", "SubscribeAsync").
    ///
    /// For recoverable errors, the transport may automatically attempt reconnection
    /// based on the configured <see cref="TransportOptions.AutoReconnect"/> setting.
    ///
    /// Event handlers should not throw exceptions, as this may disrupt error handling.
    /// </remarks>
    event EventHandler<TransportErrorEventArgs>? Error;
}

/// <summary>
/// Event arguments for transport state change notifications.
/// Contains information about the previous and current state, along with the reason for the transition.
/// </summary>
/// <param name="previousState">The state before the transition</param>
/// <param name="currentState">The state after the transition</param>
/// <param name="reason">Optional reason or description of why the state changed</param>
/// <remarks>
/// This event argument is immutable and provides a snapshot of the state transition.
/// The <see cref="OccurredAt"/> timestamp is captured when the event is created.
///
/// Example usage in event handler:
/// <code>
/// transport.StateChanged += (sender, e) =>
/// {
///     if (e.CurrentState == TransportState.Connected)
///     {
///         logger.LogInformation("Transport connected after {Duration}",
///             e.OccurredAt - startTime);
///     }
///     else if (e.CurrentState == TransportState.Faulted)
///     {
///         logger.LogError("Transport faulted. Reason: {Reason}", e.Reason);
///     }
/// };
/// </code>
/// </remarks>
public class TransportStateChangedEventArgs(TransportState previousState, TransportState currentState, string? reason = null) : EventArgs
{
    /// <summary>
    /// Gets the state before the transition.
    /// </summary>
    public TransportState PreviousState { get; } = previousState;

    /// <summary>
    /// Gets the state after the transition.
    /// </summary>
    public TransportState CurrentState { get; } = currentState;

    /// <summary>
    /// Gets the optional reason or description of why the state changed.
    /// </summary>
    /// <remarks>
    /// This may contain information such as:
    /// - "User initiated disconnect"
    /// - "Connection timeout"
    /// - "Authentication failed"
    /// - "Network unreachable"
    /// - "Broker shutdown"
    /// </remarks>
    public string? Reason { get; } = reason;

    /// <summary>
    /// Gets the UTC timestamp when the state change occurred.
    /// </summary>
    public DateTime OccurredAt { get; } = TimeProvider.System.GetUtcNow().DateTime;
}

/// <summary>
/// Event arguments for transport error notifications.
/// Contains the exception that occurred and optional context about where the error happened.
/// </summary>
/// <param name="exception">The exception that occurred</param>
/// <param name="context">Optional context describing where or why the error occurred</param>
/// <remarks>
/// This event argument captures transport-level errors such as connection failures,
/// network interruptions, authentication errors, and operation timeouts.
///
/// The context field helps identify which operation failed:
/// - "ConnectAsync" - Error during connection establishment
/// - "SendAsync" - Error sending a message
/// - "SubscribeAsync" - Error subscribing to a queue/topic
/// - "Reconnection" - Error during automatic reconnection
///
/// Example usage:
/// <code>
/// transport.Error += (sender, e) =>
/// {
///     logger.LogError(e.Exception,
///         "Transport error in {Context} at {Time}",
///         e.Context ?? "unknown", e.OccurredAt);
///
///     // Send alert for critical errors
///     if (e.Exception is AuthenticationException)
///     {
///         await alertService.SendCriticalAlertAsync(
///             "Transport authentication failed", e.Exception);
///     }
/// };
/// </code>
/// </remarks>
public class TransportErrorEventArgs(Exception exception, string? context = null) : EventArgs
{
    /// <summary>
    /// Gets the exception that occurred.
    /// </summary>
    public Exception Exception { get; } = exception;

    /// <summary>
    /// Gets the optional context describing where or why the error occurred.
    /// </summary>
    /// <remarks>
    /// Examples: "ConnectAsync", "SendAsync", "SubscribeAsync", "Reconnection"
    /// </remarks>
    public string? Context { get; } = context;

    /// <summary>
    /// Gets the UTC timestamp when the error occurred.
    /// </summary>
    public DateTime OccurredAt { get; } = TimeProvider.System.GetUtcNow().DateTime;
}

/// <summary>
/// Represents the current connection state of a message transport.
/// </summary>
/// <remarks>
/// The transport state follows this typical lifecycle:
///
/// Normal operation:
/// Disconnected -> Connecting -> Connected -> Disconnecting -> Disconnected
///
/// With auto-reconnection:
/// Connected -> Reconnecting -> Connected
///
/// Error states:
/// Any state -> Faulted (requires manual intervention or restart)
///
/// Monitor state changes via <see cref="ITransportObservability.StateChanged"/> event.
/// </remarks>
public enum TransportState
{
    /// <summary>
    /// Transport is disconnected and no connection exists.
    /// This is the initial state before any connection attempt.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - No active connection to the message broker
    /// - No messages can be sent or received
    /// - Call <see cref="IMessageTransport.ConnectAsync"/> to establish connection
    /// </remarks>
    Disconnected,

    /// <summary>
    /// Transport is attempting to establish a connection.
    /// This is a transient state during connection establishment.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - Connection handshake in progress
    /// - Authentication and protocol negotiation underway
    /// - Will transition to Connected on success or Faulted on failure
    /// - Timeout controlled by <see cref="TransportOptions.ConnectionTimeout"/>
    /// </remarks>
    Connecting,

    /// <summary>
    /// Transport is connected and ready for message operations.
    /// This is the normal operating state.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - Connection is established and authenticated
    /// - Messages can be sent and received
    /// - Subscriptions are active
    /// - May transition to Reconnecting on temporary failures
    /// - May transition to Disconnecting on graceful shutdown
    /// </remarks>
    Connected,

    /// <summary>
    /// Transport is attempting to reconnect after a connection loss.
    /// This state occurs when auto-reconnection is enabled.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - Previous connection was lost
    /// - Attempting to re-establish connection automatically
    /// - Retry policy controlled by <see cref="TransportOptions.ReconnectionPolicy"/>
    /// - Will transition to Connected on success or Faulted after max retries
    /// - In-flight operations may fail during this state
    /// </remarks>
    Reconnecting,

    /// <summary>
    /// Transport is gracefully disconnecting.
    /// This is a transient state during shutdown.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - Graceful shutdown in progress
    /// - Completing in-flight operations
    /// - Closing subscriptions and connections
    /// - Will transition to Disconnected when complete
    /// - New operations will be rejected
    /// </remarks>
    Disconnecting,

    /// <summary>
    /// Transport is in a faulted state and cannot recover automatically.
    /// This state requires manual intervention or restart.
    /// </summary>
    /// <remarks>
    /// In this state:
    /// - Transport encountered an unrecoverable error
    /// - Auto-reconnection has been exhausted (if enabled)
    /// - No operations can be performed
    /// - Check <see cref="ITransportObservability.Error"/> event for details
    /// - Requires <see cref="IMessageTransport.DisconnectAsync"/> followed by
    ///   <see cref="IMessageTransport.ConnectAsync"/> to recover
    ///
    /// Common causes:
    /// - Authentication failure
    /// - Authorization/permission errors
    /// - Invalid configuration
    /// - Broker unavailable after max retries
    /// - Network unreachable
    /// </remarks>
    Faulted
}
