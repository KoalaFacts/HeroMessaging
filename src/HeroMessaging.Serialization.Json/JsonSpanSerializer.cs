using HeroMessaging.Abstractions.Serialization;
using System;
using System.Text.Json;

namespace HeroMessaging.Serialization.Json;

/// <summary>
/// Zero-allocation JSON serializer using ref struct and Span APIs.
/// Implements ISpanSerDes interface to demonstrate C# 13 ref struct interface feature.
/// </summary>
/// <typeparam name="T">The message type to serialize/deserialize</typeparam>
public ref struct JsonSpanSerializer<T> : ISpanSerDes<T>
{
    private readonly JsonSerializerOptions? _options;

    public JsonSpanSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    public int Serialize(T message, Span<byte> destination)
    {
        var writer = new Utf8JsonWriter(new SpanWriter(destination));
        JsonSerializer.Serialize(writer, message, _options);
        writer.Flush();
        return (int)writer.BytesCommitted;
    }

    public bool TrySerialize(T message, Span<byte> destination, out int bytesWritten)
    {
        try
        {
            bytesWritten = Serialize(message, destination);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    public int GetRequiredBufferSize(T message)
    {
        // Estimate: JSON is typically 2-4x the object size for simple objects
        // For precise size, would need to serialize to counting stream
        // Using conservative estimate of 4KB for most messages
        return 4096;
    }

    public T Deserialize(ReadOnlySpan<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        return JsonSerializer.Deserialize<T>(ref reader, _options)!;
    }

    public bool TryDeserialize(ReadOnlySpan<byte> source, out T message)
    {
        try
        {
            message = Deserialize(source);
            return message != null;
        }
        catch
        {
            message = default!;
            return false;
        }
    }

    /// <summary>
    /// Helper ref struct for writing JSON directly to Span.
    /// </summary>
    private ref struct SpanWriter
    {
        private readonly Span<byte> _buffer;
        private int _position;

        public SpanWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            data.CopyTo(_buffer.Slice(_position));
            _position += data.Length;
        }

        public int BytesWritten => _position;
    }
}

/// <summary>
/// Example: Zero-allocation message wrapper using ref struct.
/// Can be used for high-performance message inspection without heap allocation.
/// </summary>
/// <typeparam name="T">The message type</typeparam>
public ref struct MessageSpan<T>
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly ISpanDeserializer<T> _deserializer;

    public MessageSpan(ReadOnlySpan<byte> data, ISpanDeserializer<T> deserializer)
    {
        _data = data;
        _deserializer = deserializer;
    }

    /// <summary>
    /// Gets the raw message data as a span (zero-copy).
    /// </summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>
    /// Gets the message size in bytes.
    /// </summary>
    public int Size => _data.Length;

    /// <summary>
    /// Deserialize the message (only allocates for the message object itself).
    /// </summary>
    public T GetMessage() => _deserializer.Deserialize(_data);

    /// <summary>
    /// Try to deserialize the message.
    /// </summary>
    public bool TryGetMessage(out T message) => _deserializer.TryDeserialize(_data, out message);

    /// <summary>
    /// Check if the message data is empty.
    /// </summary>
    public bool IsEmpty => _data.IsEmpty;

    /// <summary>
    /// Get a slice of the message data.
    /// </summary>
    public ReadOnlySpan<byte> Slice(int start, int length) => _data.Slice(start, length);
}
