namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Provides event notifications for transport state changes and errors
/// Extracted to eliminate duplication between IMessageTransport and IConnectionManager
/// </summary>
public interface ITransportObservability
{
    /// <summary>
    /// Gets the current state
    /// </summary>
    TransportState State { get; }

    /// <summary>
    /// Event raised when state changes
    /// </summary>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    event EventHandler<TransportErrorEventArgs>? Error;
}

/// <summary>
/// Event args for state changes
/// </summary>
public class TransportStateChangedEventArgs(TransportState previousState, TransportState currentState, string? reason = null) : EventArgs
{
    /// <summary>
    /// Gets previous state.
    /// </summary>
    public TransportState PreviousState { get; } = previousState;
    /// <summary>
    /// Gets current state.
    /// </summary>
    public TransportState CurrentState { get; } = currentState;
    /// <summary>
    /// Gets reason.
    /// </summary>
    public string? Reason { get; } = reason;
    /// <summary>
    /// Gets occurred at.
    /// </summary>
    public DateTimeOffset OccurredAt { get; } = TimeProvider.System.GetUtcNow();
}

/// <summary>
/// Event args for transport errors
/// </summary>
public class TransportErrorEventArgs(Exception exception, string? context = null) : EventArgs
{
    /// <summary>
    /// Gets exception.
    /// </summary>
    public Exception Exception { get; } = exception;
    /// <summary>
    /// Gets context.
    /// </summary>
    public string? Context { get; } = context;
    /// <summary>
    /// Gets occurred at.
    /// </summary>
    public DateTimeOffset OccurredAt { get; } = TimeProvider.System.GetUtcNow();
}

/// <summary>
/// Transport connection state
/// </summary>
public enum TransportState
{
    /// <summary>
    /// Specifies disconnected.
    /// </summary>
    Disconnected,
    /// <summary>
    /// Specifies connecting.
    /// </summary>
    Connecting,
    /// <summary>
    /// Specifies connected.
    /// </summary>
    Connected,
    /// <summary>
    /// Specifies reconnecting.
    /// </summary>
    Reconnecting,
    /// <summary>
    /// Specifies disconnecting.
    /// </summary>
    Disconnecting,
    /// <summary>
    /// Specifies faulted.
    /// </summary>
    Faulted
}
