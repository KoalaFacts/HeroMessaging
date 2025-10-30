using System.Collections.Immutable;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Represents a message envelope for the transport layer.
/// Contains the serialized message body and associated metadata required for message delivery and processing.
/// </summary>
/// <remarks>
/// The TransportEnvelope is optimized as a readonly record struct for:
/// - Zero heap allocations in messaging hot paths
/// - Value semantics (equality by value)
/// - Immutability for thread safety
/// - High-performance message processing loops
///
/// The envelope contains:
/// - Message payload (serialized body)
/// - Identity and tracing metadata (MessageId, CorrelationId, CausationId)
/// - Routing information (Source, Destination, ReplyTo)
/// - Message properties (Priority, TTL, DeliveryCount)
/// - Custom headers for extensibility
///
/// Metadata supports:
/// - Distributed tracing and correlation
/// - Request/response patterns
/// - Message routing and dead lettering
/// - Deduplication and idempotency
/// - Message expiration and priority queuing
///
/// Example usage:
/// <code>
/// // Create envelope with message
/// var envelope = new TransportEnvelope(
///     messageType: "CreateOrder",
///     body: messageBytes,
///     messageId: Guid.NewGuid().ToString(),
///     correlationId: sagaId)
/// {
///     Priority = 5,
///     ReplyTo = TransportAddress.Queue("order-responses")
/// };
///
/// // Add custom headers
/// envelope = envelope
///     .WithHeader("TenantId", "customer-123")
///     .WithHeader("Version", "1.0")
///     .WithTtl(TimeSpan.FromMinutes(5));
///
/// // Send message
/// await transport.SendAsync(destination, envelope);
/// </code>
///
/// Correlation tracking example:
/// <code>
/// // Initial command
/// var command = new TransportEnvelope("ProcessOrder", orderBytes)
/// {
///     CorrelationId = sagaId,  // Track entire workflow
///     CausationId = null        // No previous message
/// };
///
/// // Event caused by command
/// var @event = new TransportEnvelope("OrderProcessed", eventBytes)
/// {
///     CorrelationId = sagaId,        // Same workflow
///     CausationId = command.MessageId // Caused by command
/// };
/// </code>
/// </remarks>
public readonly record struct TransportEnvelope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransportEnvelope"/> struct with default values.
    /// </summary>
    /// <remarks>
    /// Creates an envelope with:
    /// - Auto-generated MessageId (GUID)
    /// - Empty message body
    /// - Current UTC timestamp
    /// - No expiration
    /// - Default priority (0)
    /// - No headers
    ///
    /// Typically used for testing or when all properties will be set via init syntax.
    /// </remarks>
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
        Timestamp = TimeProvider.System.GetUtcNow().DateTime;
        ExpiresAt = null;
        Priority = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportEnvelope"/> struct with message content and correlation identifiers.
    /// </summary>
    /// <param name="messageType">The fully qualified message type name or identifier</param>
    /// <param name="body">The serialized message body</param>
    /// <param name="messageId">Optional unique message identifier (auto-generated if not provided)</param>
    /// <param name="correlationId">Optional correlation identifier for linking related messages in a workflow</param>
    /// <param name="causationId">Optional causation identifier indicating which message caused this message</param>
    /// <remarks>
    /// This is the primary constructor for creating transport envelopes.
    ///
    /// Parameters:
    /// - <paramref name="messageType"/>: Used for deserialization and routing. Typically the message's fully qualified type name.
    /// - <paramref name="body"/>: Pre-serialized message bytes. Use ReadOnlyMemory for zero-copy performance.
    /// - <paramref name="messageId"/>: Unique identifier for deduplication. Auto-generated GUID if not provided.
    /// - <paramref name="correlationId"/>: Links all messages in a workflow/saga. All related messages share the same CorrelationId.
    /// - <paramref name="causationId"/>: Forms a causality chain. Set to the MessageId of the message that triggered this one.
    ///
    /// Defaults:
    /// - ContentType: "application/octet-stream"
    /// - Timestamp: Current UTC time
    /// - Priority: 0 (normal)
    /// - No expiration
    /// - No custom headers
    ///
    /// Example:
    /// <code>
    /// // Simple message
    /// var envelope = new TransportEnvelope(
    ///     "OrderCreated",
    ///     messageBytes);
    ///
    /// // With correlation tracking
    /// var envelope = new TransportEnvelope(
    ///     "OrderProcessed",
    ///     eventBytes,
    ///     messageId: Guid.NewGuid().ToString(),
    ///     correlationId: workflowId,
    ///     causationId: triggeringMessageId);
    ///
    /// // Add additional properties
    /// envelope = envelope with
    /// {
    ///     Priority = 5,
    ///     Destination = TransportAddress.Queue("high-priority-orders")
    /// };
    /// </code>
    /// </remarks>
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
        Timestamp = TimeProvider.System.GetUtcNow().DateTime;
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
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Message expiration time (TTL)
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

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
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().DateTime;
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
public static class TransportEnvelopeExtensions
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
