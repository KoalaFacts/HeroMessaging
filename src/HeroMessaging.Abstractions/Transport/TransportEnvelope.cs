using System.Collections.Immutable;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Message envelope for transport layer
/// Contains the serialized message body and metadata
/// Optimized as readonly record struct for zero-allocation scenarios
/// </summary>
public readonly record struct TransportEnvelope
{
    public TransportEnvelope()
    {
        MessageId = Guid.NewGuid().ToString();
        CorrelationId = null;
        CausationId = null;
        ConversationId = null;
        MessageType = string.Empty;
        Body = Array.Empty<byte>();
        ContentType = "application/octet-stream";
        Headers = ImmutableDictionary<string, object>.Empty;
        Timestamp = TimeProvider.System.GetUtcNow();
        ExpiresAt = null;
        Priority = 0;
    }

    public TransportEnvelope(
        string messageType,
        ReadOnlyMemory<byte> body,
        string? messageId = null,
        string? correlationId = null,
        string? causationId = null)
    {
        MessageId = messageId ?? Guid.NewGuid().ToString();
        CorrelationId = correlationId;
        CausationId = causationId;
        ConversationId = null;
        MessageType = messageType;
        Body = body;
        ContentType = "application/octet-stream";
        Headers = ImmutableDictionary<string, object>.Empty;
        Timestamp = TimeProvider.System.GetUtcNow();
        ExpiresAt = null;
        Priority = 0;
    }

    /// <summary>
    /// Unique message identifier
    /// </summary>
    public string MessageId { get; init; }

    /// <summary>
    /// Correlation identifier for linking related messages in a workflow
    /// All messages in the same workflow/saga should share the same CorrelationId
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Causation identifier indicating which message directly caused this message
    /// Forms a chain of causality for distributed tracing
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Conversation identifier for tracking related messages
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>
    /// Message type (fully qualified type name or custom identifier)
    /// </summary>
    public string MessageType { get; init; }

    /// <summary>
    /// Serialized message body
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Content type of the body (e.g., "application/json", "application/x-msgpack")
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Custom headers for metadata and routing
    /// </summary>
    public ImmutableDictionary<string, object> Headers { get; init; }

    /// <summary>
    /// Message timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Message expiration time (TTL)
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Message priority (0-255, higher is more important)
    /// </summary>
    public byte Priority { get; init; }

    /// <summary>
    /// Source address (where the message originated)
    /// </summary>
    public TransportAddress? Source { get; init; }

    /// <summary>
    /// Destination address (where the message should be delivered)
    /// </summary>
    public TransportAddress? Destination { get; init; }

    /// <summary>
    /// Reply-to address for request/response patterns
    /// </summary>
    public TransportAddress? ReplyTo { get; init; }

    /// <summary>
    /// Fault address for error handling
    /// </summary>
    public TransportAddress? FaultAddress { get; init; }

    /// <summary>
    /// Number of delivery attempts
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// Add or update a header
    /// </summary>
    public TransportEnvelope WithHeader(string key, object value)
    {
        return this with { Headers = Headers.SetItem(key, value) };
    }

    /// <summary>
    /// Add or update multiple headers
    /// </summary>
    public TransportEnvelope WithHeaders(IEnumerable<KeyValuePair<string, object>> headers)
    {
        var builder = Headers.ToBuilder();
        foreach (var header in headers)
        {
            builder[header.Key] = header.Value;
        }
        return this with { Headers = builder.ToImmutable() };
    }

    /// <summary>
    /// Set TTL (time-to-live)
    /// </summary>
    public TransportEnvelope WithTtl(TimeSpan ttl)
    {
        return this with { ExpiresAt = Timestamp.Add(ttl) };
    }

    /// <summary>
    /// Set priority
    /// </summary>
    public TransportEnvelope WithPriority(byte priority)
    {
        return this with { Priority = priority };
    }

    /// <summary>
    /// Check if the message has expired
    /// </summary>
    /// <param name="timeProvider">Optional time provider for testability. Uses system time if null.</param>
    public bool IsExpired(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return ExpiresAt.HasValue && now > ExpiresAt.Value;
    }

    /// <summary>
    /// Get header value
    /// </summary>
    public T? GetHeader<T>(string key)
    {
        return Headers.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default;
    }

    /// <summary>
    /// Check if header exists
    /// </summary>
    public bool HasHeader(string key)
    {
        return Headers.ContainsKey(key);
    }
}

/// <summary>
/// Extension methods for TransportEnvelope
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToReadOnlyMemoryForTransportEnvelope
{
    /// <summary>
    /// Create an envelope from a message type and body
    /// </summary>
    public static TransportEnvelope ToEnvelope(
        this ReadOnlyMemory<byte> body,
        string messageType,
        string? messageId = null,
        string? correlationId = null,
        string? causationId = null)
    {
        return new TransportEnvelope(messageType, body, messageId, correlationId, causationId);
    }
}
