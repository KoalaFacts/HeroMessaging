using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace HeroMessaging.Utilities;

/// <summary>
/// Default implementation of IJsonSerializer using span-based APIs.
/// Reduces allocations in hot paths like storage and validation.
/// </summary>
public sealed class DefaultJsonSerializer : IJsonSerializer
{
    private readonly DefaultBufferPoolManager _bufferPool;

    /// <summary>
    /// Creates a new instance of DefaultJsonSerializer.
    /// </summary>
    /// <param name="bufferPool">Buffer pool manager for efficient memory allocation</param>
    public DefaultJsonSerializer(DefaultBufferPoolManager bufferPool)
    {
        _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
    }

    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Uses ArrayBufferWriter for zero-allocation byte generation.
    /// </summary>
    public string SerializeToString(object value, JsonSerializerOptions? options = null)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();

        var writerOptions = new JsonWriterOptions
        {
            Indented = options?.WriteIndented ?? false,
            Encoder = options?.Encoder
        };

        using (var writer = new Utf8JsonWriter(bufferWriter, writerOptions))
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }

        // Convert UTF-8 bytes to string
        var utf8Bytes = bufferWriter.WrittenSpan;
        return Encoding.UTF8.GetString(utf8Bytes);
    }

    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Generic version for better performance when type is known at compile time.
    /// </summary>
    public string SerializeToString<T>(T value, JsonSerializerOptions? options = null)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();

        var writerOptions = new JsonWriterOptions
        {
            Indented = options?.WriteIndented ?? false,
            Encoder = options?.Encoder
        };

        using (var writer = new Utf8JsonWriter(bufferWriter, writerOptions))
        {
            JsonSerializer.Serialize(writer, value, options);
        }

        // Convert UTF-8 bytes to string
        var utf8Bytes = bufferWriter.WrittenSpan;
        return Encoding.UTF8.GetString(utf8Bytes);
    }

    /// <summary>
    /// Serializes an object to JSON and returns the UTF-8 string.
    /// Overload that accepts a runtime Type for polymorphic scenarios.
    /// </summary>
    public string SerializeToString(object value, Type type, JsonSerializerOptions? options = null)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();

        var writerOptions = new JsonWriterOptions
        {
            Indented = options?.WriteIndented ?? false,
            Encoder = options?.Encoder
        };

        using (var writer = new Utf8JsonWriter(bufferWriter, writerOptions))
        {
            JsonSerializer.Serialize(writer, value, type, options);
        }

        // Convert UTF-8 bytes to string
        var utf8Bytes = bufferWriter.WrittenSpan;
        return Encoding.UTF8.GetString(utf8Bytes);
    }

    /// <summary>
    /// Serializes an object to JSON UTF-8 bytes and writes to an ArrayBufferWriter.
    /// Allows caller to reuse the buffer or convert to string as needed.
    /// </summary>
    public void SerializeToBuffer<T>(T value, ArrayBufferWriter<byte> buffer, JsonSerializerOptions? options = null)
    {
        var writerOptions = new JsonWriterOptions
        {
            Indented = options?.WriteIndented ?? false,
            Encoder = options?.Encoder
        };

        using var writer = new Utf8JsonWriter(buffer, writerOptions);
        JsonSerializer.Serialize(writer, value, options);
    }

    /// <summary>
    /// Deserializes JSON from a UTF-8 string using span-based APIs with pooled buffers.
    /// Uses stack allocation for small JSON, pooled buffers for larger JSON.
    /// Most efficient option for deserialization hot paths.
    /// </summary>
    public T? DeserializeFromString<T>(string json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrEmpty(json))
            return default;

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);

        if (maxByteCount <= _bufferPool.SmallBufferThreshold)
        {
            // Use stack allocation for small JSON (<= 1KB)
            Span<byte> utf8Bytes = stackalloc byte[maxByteCount];
            var bytesWritten = Encoding.UTF8.GetBytes(json, utf8Bytes);

            var reader = new Utf8JsonReader(utf8Bytes.Slice(0, bytesWritten));
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }
        else
        {
            // Use pooled buffer for larger JSON
            using var pooledBuffer = _bufferPool.Rent(maxByteCount);
            var utf8Bytes = Encoding.UTF8.GetBytes(json);
            utf8Bytes.CopyTo(pooledBuffer.Span);

            var reader = new Utf8JsonReader(pooledBuffer.Span.Slice(0, utf8Bytes.Length));
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }
    }

    /// <summary>
    /// Deserializes JSON from a UTF-8 string to a specific type using pooled buffers.
    /// Uses stack allocation for small JSON, pooled buffers for larger JSON.
    /// </summary>
    public object? DeserializeFromString(string json, Type type, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);

        if (maxByteCount <= _bufferPool.SmallBufferThreshold)
        {
            // Use stack allocation for small JSON (<= 1KB)
            Span<byte> utf8Bytes = stackalloc byte[maxByteCount];
            var bytesWritten = Encoding.UTF8.GetBytes(json, utf8Bytes);

            var reader = new Utf8JsonReader(utf8Bytes.Slice(0, bytesWritten));
            return JsonSerializer.Deserialize(ref reader, type, options);
        }
        else
        {
            // Use pooled buffer for larger JSON
            using var pooledBuffer = _bufferPool.Rent(maxByteCount);
            var utf8Bytes = Encoding.UTF8.GetBytes(json);
            utf8Bytes.CopyTo(pooledBuffer.Span);

            var reader = new Utf8JsonReader(pooledBuffer.Span.Slice(0, utf8Bytes.Length));
            return JsonSerializer.Deserialize(ref reader, type, options);
        }
    }

    /// <summary>
    /// Gets the size in bytes of the JSON representation without creating a string.
    /// Useful for validation and metrics.
    /// </summary>
    public int GetJsonByteCount<T>(T value, JsonSerializerOptions? options = null)
    {
        var bufferWriter = new ArrayBufferWriter<byte>();

        var writerOptions = new JsonWriterOptions
        {
            Indented = options?.WriteIndented ?? false,
            Encoder = options?.Encoder
        };

        using (var writer = new Utf8JsonWriter(bufferWriter, writerOptions))
        {
            JsonSerializer.Serialize(writer, value, options);
        }

        return bufferWriter.WrittenCount;
    }
}
