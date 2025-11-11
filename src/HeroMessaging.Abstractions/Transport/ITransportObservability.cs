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
    public TransportState PreviousState { get; } = previousState;
    public TransportState CurrentState { get; } = currentState;
    public string? Reason { get; } = reason;
    public DateTimeOffset OccurredAt { get; } = TimeProvider.System.GetUtcNow();
}

/// <summary>
/// Event args for transport errors
/// </summary>
public class TransportErrorEventArgs(Exception exception, string? context = null) : EventArgs
{
    public Exception Exception { get; } = exception;
    public string? Context { get; } = context;
    public DateTimeOffset OccurredAt { get; } = TimeProvider.System.GetUtcNow();
}

/// <summary>
/// Transport connection state
/// </summary>
public enum TransportState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Disconnecting,
    Faulted
}
