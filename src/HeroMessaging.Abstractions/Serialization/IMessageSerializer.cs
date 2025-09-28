namespace HeroMessaging.Abstractions.Serialization;

/// <summary>
/// Interface for message serialization
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Gets the content type this serializer produces
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Serialize a message to bytes
    /// </summary>
    ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize bytes to a message
    /// </summary>
    ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deserialize bytes to a message of specified type
    /// </summary>
    ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default);
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