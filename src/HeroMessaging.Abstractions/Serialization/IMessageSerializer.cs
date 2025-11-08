namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Interface for message serialization with both async and zero-allocation span-based methods.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Gets the content type this serializer produces
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Serialize a message to bytes (async, allocates array)
    /// </summary>
    ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serialize a message to a span (zero-allocation synchronous).
    /// Returns the number of bytes written.
    /// </summary>
    int Serialize<T>(T message, Span<byte> destination);

    /// <summary>
    /// Try to serialize a message to a span.
    /// </summary>
    bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten);

    /// <summary>
    /// Get the required buffer size for serializing the message.
    /// </summary>
    int GetRequiredBufferSize<T>(T message);

    /// <summary>
    /// Deserialize bytes to a message (async)
    /// </summary>
    ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deserialize bytes to a message (zero-allocation synchronous)
    /// </summary>
    T Deserialize<T>(ReadOnlySpan<byte> data) where T : class;

    /// <summary>
    /// Deserialize bytes to a message of specified type (async)
    /// </summary>
    ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize bytes to a message of specified type (zero-allocation synchronous)
    /// </summary>
    object? Deserialize(ReadOnlySpan<byte> data, Type messageType);
}

/// <summary>
/// Serialization options
/// </summary>
public class SerializationOptions
{
    /// <summary>
    /// Whether to compress the serialized data
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Compression level (if compression is enabled)
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// Maximum message size in bytes (0 = unlimited)
    /// </summary>
    public int MaxMessageSize { get; set; }

    /// <summary>
    /// Whether to include type information in serialized data
    /// </summary>
    public bool IncludeTypeInformation { get; set; } = true;
}

public enum CompressionLevel
{
    None = 0,
    Fastest = 1,
    Optimal = 2,
    Maximum = 3
}