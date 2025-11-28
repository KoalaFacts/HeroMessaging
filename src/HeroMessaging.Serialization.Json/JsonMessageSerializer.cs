using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroMessaging.Abstractions.Serialization;

namespace HeroMessaging.Serialization.Json;

/// <summary>
/// JSON message serializer using System.Text.Json
/// </summary>
public class JsonMessageSerializer(
    SerializationOptions? options = null,
    JsonSerializerOptions? jsonOptions = null,
    ICompressionProvider? compressionProvider = null) : IMessageSerializer
{
    private readonly SerializationOptions _options = options ?? new SerializationOptions();
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? CreateDefaultOptions();
    private readonly ICompressionProvider _compressionProvider = compressionProvider ?? new GZipCompressionProvider();

    public string ContentType => "application/json";

    public async ValueTask<byte[]> SerializeAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var data = Encoding.UTF8.GetBytes(json);

        if (_options.MaxMessageSize > 0 && data.Length > _options.MaxMessageSize)
        {
            throw new InvalidOperationException($"Serialized message size ({data.Length} bytes) exceeds maximum allowed size ({_options.MaxMessageSize} bytes)");
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.CompressAsync(data, _options.CompressionLevel, cancellationToken);
        }

        return data;
    }

    public int Serialize<T>(T message, Span<byte> destination)
    {
        if (message == null) return 0;

        var bufferWriter = new ArrayBufferWriter<byte>(destination.Length);
        using var writer = new Utf8JsonWriter(bufferWriter);
        JsonSerializer.Serialize(writer, message, _jsonOptions);
        writer.Flush();

        var written = bufferWriter.WrittenSpan;
        written.CopyTo(destination);
        return written.Length;
    }

    public bool TrySerialize<T>(T message, Span<byte> destination, out int bytesWritten)
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

    public int GetRequiredBufferSize<T>(T message)
    {
        // Estimate 4KB for typical messages
        return 4096;
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.IsEmpty) return default(T)!;

        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize<T>(ref reader, _jsonOptions)!;
    }

    public object? Deserialize(ReadOnlySpan<byte> data, Type messageType)
    {
        if (data.IsEmpty) return null;

        var reader = new Utf8JsonReader(data);
        return JsonSerializer.Deserialize(ref reader, messageType, _jsonOptions);
    }

    public async ValueTask<T> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default) where T : class
    {
        if (data == null || data.Length == 0)
        {
            return default(T)!;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        var json = Encoding.UTF8.GetString(data);
        var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
        return result!;
    }

    public async ValueTask<object?> DeserializeAsync(byte[] data, Type messageType, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        if (_options.EnableCompression)
        {
            data = await _compressionProvider.DecompressAsync(data, cancellationToken);
        }

        var json = Encoding.UTF8.GetString(data);
        var result = JsonSerializer.Deserialize(json, messageType, _jsonOptions);
        return result;
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            },
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 32
        };
    }
}