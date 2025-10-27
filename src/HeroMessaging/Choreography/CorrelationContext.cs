namespace HeroMessaging.Choreography;

/// <summary>
/// Ambient context for tracking correlation and causation information during message processing
/// Uses AsyncLocal to flow context through async operations without explicit passing
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<CorrelationState?> _current = new();

    /// <summary>
    /// Gets the current correlation state
    /// </summary>
    public static CorrelationState? Current
    {
        get => _current.Value;
        private set => _current.Value = value;
    }

    /// <summary>
    /// Gets the current CorrelationId if available
    /// </summary>
    public static string? CurrentCorrelationId => Current?.CorrelationId;

    /// <summary>
    /// Gets the current MessageId (which becomes the CausationId for new messages)
    /// </summary>
    public static string? CurrentMessageId => Current?.MessageId;

    /// <summary>
    /// Sets the correlation context from an incoming message
    /// </summary>
    /// <param name="correlationId">Correlation identifier from the message</param>
    /// <param name="messageId">Message identifier (becomes CausationId for descendant messages)</param>
    /// <returns>Disposable scope for automatic cleanup</returns>
    public static IDisposable BeginScope(string? correlationId, string messageId)
    {
        var previousState = Current;
        Current = new CorrelationState(correlationId, messageId);
        return new CorrelationScope(previousState);
    }

    /// <summary>
    /// Creates a correlation context scope from a message
    /// </summary>
    public static IDisposable BeginScope(Abstractions.Messages.IMessage message)
    {
        return BeginScope(
            message.CorrelationId ?? message.MessageId.ToString(),
            message.MessageId.ToString()
        );
    }

    /// <summary>
    /// Clears the current correlation context
    /// </summary>
    internal static void Clear()
    {
        Current = null;
    }

    /// <summary>
    /// Disposable scope for automatic context cleanup
    /// </summary>
    private class CorrelationScope : IDisposable
    {
        private readonly CorrelationState? _previousState;
        private bool _disposed;

        public CorrelationScope(CorrelationState? previousState)
        {
            _previousState = previousState;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Current = _previousState;
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Immutable state for correlation tracking
/// </summary>
/// <param name="CorrelationId">Correlation identifier linking related messages</param>
/// <param name="MessageId">Current message ID (becomes CausationId for descendant messages)</param>
public record CorrelationState(string? CorrelationId, string MessageId);
