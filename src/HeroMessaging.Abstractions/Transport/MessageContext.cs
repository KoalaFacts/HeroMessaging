using System.Collections.Immutable;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Context for message processing in transport layer
/// Optimized as readonly record struct for zero allocations
/// </summary>
public readonly record struct MessageContext
{
    public MessageContext()
    {
        TransportName = string.Empty;
        SourceAddress = default;
        ReceiveTimestamp = DateTime.UtcNow;
        Properties = ImmutableDictionary<string, object>.Empty;
    }

    public MessageContext(string transportName, TransportAddress sourceAddress)
    {
        TransportName = transportName;
        SourceAddress = sourceAddress;
        ReceiveTimestamp = DateTime.UtcNow;
        Properties = ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Name of the transport that received the message
    /// </summary>
    public string TransportName { get; init; }

    /// <summary>
    /// Source address where the message was received from
    /// </summary>
    public TransportAddress SourceAddress { get; init; }

    /// <summary>
    /// When the message was received by the transport
    /// </summary>
    public DateTime ReceiveTimestamp { get; init; }

    /// <summary>
    /// Transport-specific properties
    /// </summary>
    public ImmutableDictionary<string, object> Properties { get; init; }

    /// <summary>
    /// Acknowledgment callback
    /// </summary>
    public Func<CancellationToken, Task>? Acknowledge { get; init; }

    /// <summary>
    /// Negative acknowledgment callback (reject/nack)
    /// </summary>
    public Func<bool, CancellationToken, Task>? Reject { get; init; }

    /// <summary>
    /// Defer/defer message processing callback
    /// </summary>
    public Func<TimeSpan?, CancellationToken, Task>? Defer { get; init; }

    /// <summary>
    /// Dead letter callback
    /// </summary>
    public Func<string?, CancellationToken, Task>? DeadLetter { get; init; }

    /// <summary>
    /// Add or update a property
    /// </summary>
    public MessageContext WithProperty(string key, object value)
    {
        return this with { Properties = Properties.SetItem(key, value) };
    }

    /// <summary>
    /// Get a property value
    /// </summary>
    public T? GetProperty<T>(string key)
    {
        return Properties.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default;
    }

    /// <summary>
    /// Acknowledge the message (complete processing)
    /// </summary>
    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
    {
        return Acknowledge?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Reject the message (nack)
    /// </summary>
    /// <param name="requeue">Whether to requeue the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task RejectAsync(bool requeue = false, CancellationToken cancellationToken = default)
    {
        return Reject?.Invoke(requeue, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Defer message processing
    /// </summary>
    /// <param name="delay">Delay before retry (null for transport default)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task DeferAsync(TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        return Defer?.Invoke(delay, cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Move message to dead letter queue
    /// </summary>
    /// <param name="reason">Reason for dead lettering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task DeadLetterAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        return DeadLetter?.Invoke(reason, cancellationToken) ?? Task.CompletedTask;
    }
}
