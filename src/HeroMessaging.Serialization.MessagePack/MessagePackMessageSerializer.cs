using MessagePack;
using MessagePack.Resolvers;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization;

namespace HeroMessaging.Serialization.MessagePack;

/// <summary>
/// MessagePack serializer for high-performance binary serialization
/// </summary>
public class MessagePackMessageSerializer : BaseMessageSerializer
{
    private readonly MessagePackSerializerOptions _messagePackOptions;
    
    public MessagePackMessageSerializer(SerializationOptions? options = null, MessagePackSerializerOptions? messagePackOptions = null) 
        : base(options)
    {
        _messagePackOptions = messagePackOptions ?? CreateDefaultOptions();
    }
    
    public override string ContentType => "application/x-msgpack";
    
    protected override ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken)
    {
        var bytes = MessagePackSerializer.Serialize(message, _messagePackOptions, cancellationToken);
        return new ValueTask<byte[]>(bytes);
    }
    
    protected override ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class
    {
        var result = MessagePackSerializer.Deserialize<T>(data, _messagePackOptions, cancellationToken);
        return new ValueTask<T>(result!);
    }
    
    protected override ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken)
    {
        var result = MessagePackSerializer.Deserialize(messageType, data, _messagePackOptions, cancellationToken);
        return new ValueTask<object?>(result);
    }
    
    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        return MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }
}

/// <summary>
/// MessagePack serializer with type-safe contracts (requires MessagePack attributes)
/// </summary>
public class ContractMessagePackSerializer : BaseMessageSerializer
{
    private readonly MessagePackSerializerOptions _messagePackOptions;
    
    public ContractMessagePackSerializer(SerializationOptions? options = null, MessagePackSerializerOptions? messagePackOptions = null) 
        : base(options)
    {
        _messagePackOptions = messagePackOptions ?? CreateDefaultOptions();
    }
    
    public override string ContentType => "application/x-msgpack-contract";
    
    protected override ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken)
    {
        var bytes = MessagePackSerializer.Serialize(message, _messagePackOptions, cancellationToken);
        return new ValueTask<byte[]>(bytes);
    }
    
    protected override ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class
    {
        var result = MessagePackSerializer.Deserialize<T>(data, _messagePackOptions, cancellationToken);
        return new ValueTask<T>(result!);
    }
    
    protected override ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken)
    {
        var result = MessagePackSerializer.Deserialize(messageType, data, _messagePackOptions, cancellationToken);
        return new ValueTask<object?>(result);
    }
    
    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        // Use standard resolver which requires MessagePack attributes for better performance
        return MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }
}