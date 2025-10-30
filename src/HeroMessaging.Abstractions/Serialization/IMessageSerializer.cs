namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Interface for high-performance message serialization and deserialization.
/// Enables pluggable serialization formats (JSON, MessagePack, Protobuf, etc.) for message transport and storage.
/// </summary>
/// <remarks>
/// Implement this interface to provide custom serialization formats for HeroMessaging.
/// The framework uses serializers to:
/// - Convert messages to bytes for transport (queue, network, event bus)
/// - Store messages in storage backends (outbox, inbox, queue tables)
/// - Enable cross-language message compatibility
///
/// Design Principles:
/// - Uses ValueTask for zero-allocation async operations
/// - Supports both generic and non-generic deserialization
/// - Thread-safe for concurrent serialization/deserialization
/// - Should handle null values gracefully
///
/// Built-in implementations:
/// - JsonMessageSerializer: Human-readable JSON format (default)
/// - MessagePackMessageSerializer: Binary format for high performance
/// - ProtobufMessageSerializer: Protocol Buffers for cross-platform compatibility
///
/// <code>
/// // Register custom serializer
/// services.AddHeroMessaging(builder =>
/// {
///     builder.UseSerializer&lt;MessagePackMessageSerializer&gt;();
/// });
///
/// // Use serializer directly
/// var serializer = serviceProvider.GetRequiredService&lt;IMessageSerializer&gt;();
/// var bytes = await serializer.SerializeAsync(message);
/// var deserialized = await serializer.DeserializeAsync&lt;MyMessage&gt;(bytes);
/// </code>
///
/// Performance considerations:
/// - Target: &lt;100μs for typical messages (&lt;10KB)
/// - Minimize allocations (use buffer pools, spans where possible)
/// - Consider compression for large messages (see SerializationOptions)
/// - Use source generators for compile-time serialization (System.Text.Json)
/// </remarks>
public interface IMessageSerializer
{
    /// <summary>
    /// Gets the MIME content type produced by this serializer.
    /// Used for HTTP transport headers and message type identification.
    /// </summary>
    /// <remarks>
    /// Standard content types:
    /// - "application/json": JSON format
    /// - "application/x-msgpack": MessagePack format
    /// - "application/x-protobuf": Protocol Buffers format
    /// - "application/octet-stream": Generic binary format
    ///
    /// This value is:
    /// - Written to transport headers (Content-Type)
    /// - Used for serializer selection when receiving messages
    /// - Stored with messages in outbox/inbox tables
    ///
    /// <code>
    /// var serializer = serviceProvider.GetRequiredService&lt;IMessageSerializer&gt;();
    /// Console.WriteLine($"Using serializer: {serializer.ContentType}");
    /// // Output: "Using serializer: application/json"
    /// </code>
    /// </remarks>
    string ContentType { get; }

    /// <summary>
    /// Serializes a message to a byte array for transport or storage.
    /// </summary>
    /// <typeparam name="T">The type of message to serialize.</typeparam>
    /// <param name="message">The message instance to serialize. May be null depending on serializer implementation.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A ValueTask containing the serialized message as a byte array.
    /// The byte array can be sent over the network, stored in a database, or written to a queue.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null and serializer doesn't support null values.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when serialization fails (invalid type, circular reference, etc.).</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method should:
    /// - Convert the message object to the serializer's format (JSON, binary, etc.)
    /// - Apply any configured compression (see SerializationOptions)
    /// - Include type information if configured (for polymorphic deserialization)
    /// - Complete in &lt;100μs for typical messages
    ///
    /// Implementation guidelines:
    /// - Use buffer pools to reduce allocations (ArrayPool&lt;byte&gt;)
    /// - Support polymorphic serialization (derived types)
    /// - Handle circular references gracefully (throw or configure limits)
    /// - Respect cancellation tokens for large messages
    ///
    /// <code>
    /// var message = new OrderCreatedEvent("ORD-123", "CUST-001", 99.99m);
    /// var bytes = await serializer.SerializeAsync(message, cancellationToken);
    ///
    /// // bytes can now be:
    /// // - Sent to message broker
    /// // - Stored in outbox table
    /// // - Enqueued for background processing
    /// </code>
    /// </remarks>
    ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes a byte array to a strongly-typed message instance.
    /// </summary>
    /// <typeparam name="T">The expected type of the deserialized message. Must be a reference type.</typeparam>
    /// <param name="data">The serialized message bytes to deserialize.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A ValueTask containing the deserialized message instance of type T.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    /// <exception cref="ArgumentException">Thrown when data is empty or invalid.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when deserialization fails (invalid format, type mismatch, version incompatibility, etc.).
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method should:
    /// - Parse the byte array according to the serializer's format
    /// - Apply any configured decompression
    /// - Validate the type matches T (or is assignable to T)
    /// - Complete in &lt;100μs for typical messages
    ///
    /// Implementation guidelines:
    /// - Use buffer pools to reduce allocations
    /// - Validate message format before full deserialization
    /// - Support schema evolution (backward/forward compatibility)
    /// - Provide clear error messages for deserialization failures
    ///
    /// <code>
    /// // Deserialize from queue, transport, or storage
    /// var bytes = await ReceiveFromQueueAsync();
    /// var message = await serializer.DeserializeAsync&lt;OrderCreatedEvent&gt;(bytes);
    ///
    /// // Type safety: compiler knows message is OrderCreatedEvent
    /// Console.WriteLine($"Order {message.OrderId} created");
    /// </code>
    /// </remarks>
    ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deserializes a byte array to a message instance of the specified runtime type.
    /// Used when the message type is not known at compile time (polymorphic deserialization).
    /// </summary>
    /// <param name="data">The serialized message bytes to deserialize.</param>
    /// <param name="messageType">The runtime type to deserialize to. Must be a valid message type.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A ValueTask containing the deserialized message instance as object (requires casting).
    /// Returns null if the serialized data represents a null value.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when data or messageType is null.</exception>
    /// <exception cref="ArgumentException">Thrown when data is empty, invalid, or messageType is not a valid message type.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when deserialization fails (invalid format, type mismatch, version incompatibility, etc.).
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This overload is used by the framework when:
    /// - Message type is determined at runtime (from metadata, headers, or type discriminator)
    /// - Deserializing from storage where type information is stored separately
    /// - Implementing polymorphic message handlers
    ///
    /// The caller must cast the result to the appropriate type:
    ///
    /// <code>
    /// // Type determined at runtime from metadata
    /// var messageTypeName = messageMetadata["MessageType"];
    /// var messageType = Type.GetType(messageTypeName);
    ///
    /// var message = await serializer.DeserializeAsync(bytes, messageType);
    ///
    /// // Cast to expected type
    /// if (message is OrderCreatedEvent orderEvent)
    /// {
    ///     Console.WriteLine($"Order {orderEvent.OrderId} created");
    /// }
    /// </code>
    ///
    /// Implementation notes:
    /// - Use reflection or compiled expressions for type instantiation
    /// - Support type inheritance and polymorphism
    /// - Validate messageType is compatible with serialized data
    /// - Consider caching type metadata for performance
    /// </remarks>
    ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for message serialization behavior.
/// Controls compression, size limits, and type information handling.
/// </summary>
/// <remarks>
/// Use these options to:
/// - Enable compression for large messages (reduces transport/storage costs)
/// - Enforce message size limits (prevent out-of-memory or DOS attacks)
/// - Control type information serialization (polymorphism vs size)
///
/// <code>
/// // Configure serialization in DI
/// services.AddHeroMessaging(builder =>
/// {
///     builder.UseJsonSerialization(options =>
///     {
///         options.EnableCompression = true;
///         options.CompressionLevel = CompressionLevel.Optimal;
///         options.MaxMessageSize = 1024 * 1024; // 1MB limit
///         options.IncludeTypeInformation = true; // For polymorphic types
///     });
/// });
/// </code>
/// </remarks>
public class SerializationOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to compress serialized messages.
    /// Default is false (no compression).
    /// </summary>
    /// <remarks>
    /// Enable compression when:
    /// - Messages are large (&gt;1KB) and compressible (text, JSON, XML)
    /// - Network bandwidth is limited or expensive
    /// - Storage costs are a concern
    ///
    /// Disable compression when:
    /// - Messages are small (&lt;1KB) - compression overhead exceeds benefits
    /// - Messages are already compressed (images, video, encrypted data)
    /// - CPU is more constrained than bandwidth
    /// - Latency is critical (&lt;1ms target)
    ///
    /// Compression impact:
    /// - JSON messages: 60-90% size reduction typical
    /// - Binary messages: 0-50% size reduction (depends on data entropy)
    /// - CPU overhead: 10-100μs additional latency per message
    ///
    /// The compression algorithm used depends on the serializer implementation
    /// (typically GZip or Brotli for HTTP, LZ4 for high-performance scenarios).
    /// </remarks>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Gets or sets the compression level to use when compression is enabled.
    /// Default is CompressionLevel.Optimal (balanced size vs speed).
    /// </summary>
    /// <remarks>
    /// Only applies when EnableCompression is true.
    ///
    /// Compression level trade-offs:
    /// - None: No compression (same as EnableCompression = false)
    /// - Fastest: Lowest compression ratio, fastest speed (~5μs overhead)
    /// - Optimal: Balanced ratio and speed (~25μs overhead) - RECOMMENDED
    /// - Maximum: Best compression ratio, slowest speed (~100μs overhead)
    ///
    /// Choose based on your constraints:
    /// - Fastest: When CPU is limited but some compression is beneficial
    /// - Optimal: Default choice for most scenarios
    /// - Maximum: When bandwidth/storage costs dominate (archival, slow networks)
    ///
    /// <code>
    /// // High-throughput scenarios
    /// options.CompressionLevel = CompressionLevel.Fastest;
    ///
    /// // Bandwidth-constrained scenarios (mobile, satellite)
    /// options.CompressionLevel = CompressionLevel.Maximum;
    /// </code>
    /// </remarks>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets the maximum allowed message size in bytes.
    /// Default is 0 (unlimited). Set to positive value to enforce size limits.
    /// </summary>
    /// <remarks>
    /// Use this to:
    /// - Prevent out-of-memory errors from malicious or malformed messages
    /// - Enforce architectural constraints (e.g., "messages must be &lt;1MB")
    /// - Detect data modeling issues (messages that are too large)
    /// - Protect against denial-of-service attacks
    ///
    /// When a message exceeds this limit:
    /// - Serialization throws ArgumentException with clear error message
    /// - Deserialization validates size before parsing (prevents DOS)
    ///
    /// Recommended limits:
    /// - Commands/Events: 10KB - 100KB (should be small and focused)
    /// - Queries: 10KB (request) / 1MB (response with data)
    /// - Queue messages: 256KB - 1MB (transport limits vary)
    /// - Outbox/Inbox: 1MB - 10MB (database storage constraints)
    ///
    /// If you regularly exceed these limits, consider:
    /// - Using references instead of embedding data (send ID, not full object)
    /// - Chunking large payloads into multiple messages
    /// - Using blob storage for large data (send blob URL in message)
    /// - Reviewing domain model design
    ///
    /// <code>
    /// // Enforce strict size limits
    /// options.MaxMessageSize = 100 * 1024; // 100KB maximum
    ///
    /// // Unlimited (default, use with caution)
    /// options.MaxMessageSize = 0;
    /// </code>
    /// </remarks>
    public int MaxMessageSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include .NET type information in serialized messages.
    /// Default is true (include type information for polymorphic deserialization).
    /// </summary>
    /// <remarks>
    /// Type information enables:
    /// - Polymorphic deserialization (deserialize base class to derived type)
    /// - Runtime type discovery (know message type without external metadata)
    /// - Cross-version compatibility (handle type renames, migrations)
    ///
    /// Type information costs:
    /// - Additional 50-200 bytes per message (type name, assembly)
    /// - Slightly slower deserialization (type resolution)
    /// - Potential security concerns (type information disclosure)
    ///
    /// Set to true when:
    /// - Using polymorphic message types (base classes, interfaces)
    /// - Message type varies and must be discovered at runtime
    /// - Using .NET-to-.NET communication only
    ///
    /// Set to false when:
    /// - All messages have known, concrete types
    /// - Communicating across languages/platforms (Java, Go, etc.)
    /// - Type information is provided via metadata (headers, envelope)
    /// - Minimizing message size is critical
    /// - Security policy prohibits type disclosure
    ///
    /// <code>
    /// // Include type info for polymorphism (.NET only)
    /// options.IncludeTypeInformation = true;
    ///
    /// // Exclude type info for cross-platform
    /// options.IncludeTypeInformation = false;
    ///
    /// // Example: Polymorphic event handling
    /// public interface IEvent { }
    /// public class OrderCreatedEvent : IEvent { }
    /// public class OrderCancelledEvent : IEvent { }
    ///
    /// // With IncludeTypeInformation = true:
    /// // Serializer includes "$type": "OrderCreatedEvent"
    /// // Can deserialize as IEvent and discover actual type at runtime
    ///
    /// // With IncludeTypeInformation = false:
    /// // Must specify concrete type at deserialization
    /// // Better for cross-language scenarios
    /// </code>
    /// </remarks>
    public bool IncludeTypeInformation { get; set; } = true;
}

/// <summary>
/// Specifies the compression level for message serialization.
/// Provides trade-offs between compression ratio (size reduction) and CPU cost (speed).
/// </summary>
/// <remarks>
/// Compression levels are ordered by increasing compression ratio and CPU cost:
/// None &lt; Fastest &lt; Optimal &lt; Maximum
///
/// Use this enum to configure SerializationOptions.CompressionLevel.
/// The actual compression algorithm (GZip, Brotli, LZ4, etc.) depends on
/// the serializer implementation.
/// </remarks>
public enum CompressionLevel
{
    /// <summary>
    /// No compression applied. Same as disabling compression entirely.
    /// </summary>
    /// <remarks>
    /// Performance: 0μs overhead
    /// Compression ratio: 0% (no reduction)
    ///
    /// Use when:
    /// - Messages are very small (&lt;1KB)
    /// - Messages are already compressed or encrypted
    /// - CPU is extremely constrained
    /// - Sub-microsecond latency is required
    /// </remarks>
    None = 0,

    /// <summary>
    /// Fastest compression with minimal CPU overhead.
    /// Prioritizes speed over compression ratio.
    /// </summary>
    /// <remarks>
    /// Performance: ~5μs overhead (typical)
    /// Compression ratio: 30-60% size reduction (varies by content)
    ///
    /// Use when:
    /// - High throughput is critical (&gt;10K msg/s)
    /// - Some compression is beneficial but CPU is limited
    /// - Latency budget is tight (still need &lt;10μs)
    ///
    /// Example scenarios:
    /// - High-frequency trading systems
    /// - Real-time telemetry/metrics
    /// - Internal service-to-service communication
    /// </remarks>
    Fastest = 1,

    /// <summary>
    /// Balanced compression offering good ratio with reasonable CPU cost.
    /// This is the recommended default for most scenarios.
    /// </summary>
    /// <remarks>
    /// Performance: ~25μs overhead (typical)
    /// Compression ratio: 60-80% size reduction (varies by content)
    ///
    /// Use when:
    /// - Balanced performance and efficiency is desired (most scenarios)
    /// - Moderate message volume (&lt;10K msg/s)
    /// - Both bandwidth and CPU are moderate concerns
    ///
    /// Example scenarios:
    /// - Web APIs and microservices
    /// - Message queues and event buses
    /// - Outbox/inbox pattern storage
    /// - General-purpose messaging
    /// </remarks>
    Optimal = 2,

    /// <summary>
    /// Maximum compression achieving the best ratio at highest CPU cost.
    /// Prioritizes compression ratio over speed.
    /// </summary>
    /// <remarks>
    /// Performance: ~100μs overhead (typical)
    /// Compression ratio: 70-90% size reduction (varies by content)
    ///
    /// Use when:
    /// - Bandwidth/storage costs are the primary concern
    /// - Low message volume (&lt;1K msg/s)
    /// - Latency is not critical (&gt;100μs acceptable)
    /// - Archival or long-term storage
    ///
    /// Example scenarios:
    /// - Satellite or mobile network communication
    /// - Archive/audit logging
    /// - Infrequent large reports or exports
    /// - Bandwidth-metered cloud services
    /// </remarks>
    Maximum = 3
}