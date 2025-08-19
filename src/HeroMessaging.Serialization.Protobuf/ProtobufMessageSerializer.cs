using ProtoBuf;
using ProtoBuf.Meta;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization;

namespace HeroMessaging.Serialization.Protobuf;

/// <summary>
/// Protocol Buffers serializer for efficient binary serialization
/// </summary>
public class ProtobufMessageSerializer : BaseMessageSerializer
{
    private readonly RuntimeTypeModel _typeModel;
    
    public ProtobufMessageSerializer(SerializationOptions? options = null, RuntimeTypeModel? typeModel = null) 
        : base(options)
    {
        _typeModel = typeModel ?? RuntimeTypeModel.Default;
    }
    
    public override string ContentType => "application/x-protobuf";
    
    protected override ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        _typeModel.Serialize(stream, message);
        return new ValueTask<byte[]>(stream.ToArray());
    }
    
    protected override ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class
    {
        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize<T>(stream);
        return new ValueTask<T>(result!);
    }
    
    protected override ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data);
        var result = _typeModel.Deserialize(stream, null, messageType);
        return new ValueTask<object?>(result);
    }
}

/// <summary>
/// Protobuf serializer with type information included for polymorphic scenarios
/// </summary>
public class TypedProtobufMessageSerializer : BaseMessageSerializer
{
    private readonly RuntimeTypeModel _typeModel;
    
    public TypedProtobufMessageSerializer(SerializationOptions? options = null, RuntimeTypeModel? typeModel = null) 
        : base(options)
    {
        _typeModel = typeModel ?? RuntimeTypeModel.Default;
    }
    
    public override string ContentType => "application/x-protobuf-typed";
    
    protected override ValueTask<byte[]> SerializeCore<T>(T message, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        
        if (Options.IncludeTypeInformation && message != null)
        {
            // Write type information
            var typeName = message.GetType().AssemblyQualifiedName ?? "";
            Serializer.SerializeWithLengthPrefix(stream, typeName, PrefixStyle.Base128);
        }
        
        // Write the actual message
        _typeModel.SerializeWithLengthPrefix(stream, message, typeof(T), PrefixStyle.Base128, 0);
        
        return new ValueTask<byte[]>(stream.ToArray());
    }
    
    protected override ValueTask<T> DeserializeCore<T>(byte[] data, CancellationToken cancellationToken) where T : class
    {
        using var stream = new MemoryStream(data);
        
        if (Options.IncludeTypeInformation)
        {
            // Skip type information if present
            Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
        }
        
        var result = (T?)_typeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Base128, 0);
        return new ValueTask<T>(result!);
    }
    
    protected override ValueTask<object?> DeserializeCore(byte[] data, Type messageType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data);
        
        if (Options.IncludeTypeInformation)
        {
            // Read type information
            var typeName = Serializer.DeserializeWithLengthPrefix<string>(stream, PrefixStyle.Base128);
            if (!string.IsNullOrEmpty(typeName))
            {
                var actualType = Type.GetType(typeName);
                if (actualType != null)
                {
                    messageType = actualType;
                }
            }
        }
        
        var result = _typeModel.DeserializeWithLengthPrefix(stream, null, messageType, PrefixStyle.Base128, 0);
        return new ValueTask<object?>(result);
    }
}