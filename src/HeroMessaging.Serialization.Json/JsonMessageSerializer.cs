using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization;

namespace HeroMessaging.Serialization.Json;

/// <summary>
/// JSON message serializer using System.Text.Json
/// </summary>
public class JsonMessageSerializer : BaseMessageSerializer
{
    private readonly JsonSerializerOptions _jsonOptions;
    
    public JsonMessageSerializer(SerializationOptions? options = null, JsonSerializerOptions? jsonOptions = null) 
        : base(options)
    {
        _jsonOptions = jsonOptions ?? CreateDefaultOptions();
    }
    
    public override string ContentType => "application/json";
    
    protected override ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ValueTask<byte[]>(bytes);
    }
    
    protected override ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class
    {
        var json = Encoding.UTF8.GetString(data);
        var result = JsonSerializer.Deserialize<T>(json, _jsonOptions);
        return new ValueTask<T>(result!);
    }
    
    protected override ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(data);
        var result = JsonSerializer.Deserialize(json, messageType, _jsonOptions);
        return new ValueTask<object?>(result);
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