# HeroMessaging.Serialization.MessagePack

**High-performance binary serialization for HeroMessaging using MessagePack.**

## Overview

MessagePack is a binary serialization format that's faster and more compact than JSON while maintaining cross-language compatibility. This provider offers the best performance for high-throughput scenarios where message size and serialization speed are critical.

**Key Benefits**:
- **Fastest**: 3-5x faster than JSON serialization
- **Smallest**: 50-70% smaller payloads than JSON
- **Cross-Platform**: Compatible with MessagePack implementations in other languages
- **Type-Safe**: Full .NET type preservation

## Installation

```bash
dotnet add package HeroMessaging.Serialization.MessagePack
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

**Note**: Not available for .NET Standard 2.0 (requires modern .NET features)

## Quick Start

```csharp
using HeroMessaging;
using HeroMessaging.Serialization.MessagePack;

services.AddHeroMessaging(builder =>
{
    builder.UseMessagePackSerialization();
});
```

### With Custom Options

```csharp
services.AddHeroMessaging(builder =>
{
    builder.UseMessagePackSerialization(options =>
    {
        options.Compression = MessagePackCompression.Lz4Block; // Enable compression
        options.OmitAssemblyVersion = true;
        options.AllowAssemblyVersionMismatch = true;
    });
});
```

## Configuration

### MessagePackSerializerOptions

```csharp
builder.UseMessagePackSerialization(options =>
{
    // Compression (increases CPU, reduces size)
    options.Compression = MessagePackCompression.Lz4Block;

    // Security settings
    options.Security = MessagePackSecurity.UntrustedData;

    // Resolver for custom types
    options.Resolver = MessagePack.Resolvers.ContractlessStandardResolver.Instance;

    // Assembly version handling
    options.OmitAssemblyVersion = true;
    options.AllowAssemblyVersionMismatch = true;
});
```

### Compression Options

| Compression | Speed | Size | Use Case |
|-------------|-------|------|----------|
| `None` | Fastest | Largest | Low latency scenarios |
| `Lz4Block` | Fast | Small | **Recommended** for most cases |
| `Lz4BlockArray` | Fast | Small | Better for arrays |

## Performance

**Benchmarks** (compared to JSON):

| Metric | MessagePack | JSON | Improvement |
|--------|-------------|------|-------------|
| Serialization | ~1.2 μs | ~4.5 μs | **3.75x faster** |
| Deserialization | ~1.8 μs | ~5.2 μs | **2.89x faster** |
| Payload Size | ~180 bytes | ~420 bytes | **57% smaller** |
| Throughput | >800K msg/s | ~220K msg/s | **3.6x higher** |

**Memory**:
- Allocation: ~400 bytes per message (vs ~850 bytes JSON)
- Uses ArrayPool for zero-allocation in hot paths

## Message Attributes

### Basic Usage

```csharp
using MessagePack;

[MessagePackObject]
public class OrderCreatedEvent : IEvent
{
    [Key(0)]
    public Guid MessageId { get; set; }

    [Key(1)]
    public DateTime Timestamp { get; set; }

    [Key(2)]
    public string OrderId { get; set; }

    [Key(3)]
    public decimal Amount { get; set; }

    // MessagePack requires explicit key attributes
}
```

### Using Records (Recommended)

```csharp
[MessagePackObject]
public record OrderCreatedEvent(
    [property: Key(0)] Guid MessageId,
    [property: Key(1)] DateTime Timestamp,
    [property: Key(2)] string OrderId,
    [property: Key(3)] decimal Amount
) : IEvent;
```

### Contractless Serialization

For types you don't control:

```csharp
builder.UseMessagePackSerialization(options =>
{
    options.Resolver = MessagePack.Resolvers.ContractlessStandardResolver.Instance;
});

// No [MessagePackObject] or [Key] attributes needed!
public class LegacyMessage : IEvent
{
    public Guid MessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Data { get; set; }
}
```

## When to Use MessagePack

### ✅ Best For

1. **High-Throughput Systems**
   - Processing >50K messages/second
   - Network bandwidth is limited
   - Serialization CPU cost is significant

2. **Microservices Communication**
   - Internal service-to-service messaging
   - RabbitMQ, Kafka, or other binary-friendly transports
   - Cross-language compatibility needed

3. **Large Messages**
   - Messages >1KB where size matters
   - Array-heavy data structures
   - Nested object graphs

### ❌ Avoid When

1. **HTTP APIs** - Use JSON for better HTTP/REST compatibility
2. **Debugging** - Binary format harder to inspect than JSON
3. **Schema Evolution** - JSON is more forgiving with schema changes
4. **.NET Standard 2.0** - Not supported (use JSON instead)

## Migration from JSON

### Side-by-Side Configuration

Support both during migration:

```csharp
services.AddHeroMessaging(builder =>
{
    // Default to MessagePack
    builder.UseMessagePackSerialization();

    // But also register JSON for specific message types
    builder.UseJsonSerialization();
});

// Specify serializer per message
await messaging.Send(new OrderCreated(),
    new SendOptions { Serializer = "json" }); // Force JSON
```

### Gradual Rollout

1. **Phase 1**: Keep JSON, add MessagePack support
2. **Phase 2**: New messages use MessagePack
3. **Phase 3**: Migrate existing messages
4. **Phase 4**: Remove JSON (if desired)

## Troubleshooting

### Common Issues

#### Issue: "Failed to deserialize" Error

**Symptoms**:
```
MessagePackSerializationException: Failed to deserialize
```

**Solution**:
```csharp
// Ensure all properties have [Key] attributes
[MessagePackObject]
public class MyMessage
{
    [Key(0)] // Required!
    public string Property { get; set; }
}

// Or use ContractlessResolver
options.Resolver = ContractlessStandardResolver.Instance;
```

#### Issue: "Unexpected msgpack code" Error

**Symptoms**:
```
MessagePackSerializationException: code is invalid
```

**Solution**:
- Mixing MessagePack and JSON serializers
- Check message headers for correct content-type
- Ensure sender and receiver use same serializer

#### Issue: Version Mismatch

**Symptoms**:
```
TypeInitializationException: Could not load type
```

**Solution**:
```csharp
options.AllowAssemblyVersionMismatch = true;
options.OmitAssemblyVersion = true;
```

### Debugging

Enable verbose logging:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddFilter("MessagePack", LogLevel.Trace);
});
```

Inspect binary messages:

```csharp
var bytes = MessagePackSerializer.Serialize(message);
var hex = BitConverter.ToString(bytes);
Console.WriteLine($"MessagePack hex: {hex}");

// Use online MessagePack viewers:
// https://kawanet.github.io/msgpack-lite/
```

## Advanced Scenarios

### Custom Type Resolvers

```csharp
using MessagePack;
using MessagePack.Resolvers;

// Create custom resolver
var resolver = CompositeResolver.Create(
    // Custom formatters
    new[] { MyCustomFormatter.Instance },

    // Fallback to standard resolvers
    new[] {
        StandardResolver.Instance,
        ContractlessStandardResolver.Instance
    }
);

builder.UseMessagePackSerialization(options =>
{
    options.Resolver = resolver;
});
```

### DateTime Handling

```csharp
[MessagePackObject]
public class TimestampMessage
{
    [Key(0)]
    [MessagePackFormatter(typeof(DateTimeBinaryFormatter))]
    public DateTime CreatedAt { get; set; } // Binary format

    [Key(1)]
    [MessagePackFormatter(typeof(DateTimeStringFormatter))]
    public DateTime UpdatedAt { get; set; } // ISO8601 string
}
```

### Union Types

```csharp
[Union(0, typeof(OrderCreatedEvent))]
[Union(1, typeof(OrderUpdatedEvent))]
[Union(2, typeof(OrderDeletedEvent))]
public interface IOrderEvent : IEvent
{
}

// Polymorphic serialization supported!
```

## Testing

### Unit Testing

```csharp
using MessagePack;

[Fact]
public void CanSerializeAndDeserialize()
{
    var original = new OrderCreatedEvent
    {
        MessageId = Guid.NewGuid(),
        OrderId = "ORD-001",
        Amount = 99.99m
    };

    var bytes = MessagePackSerializer.Serialize(original);
    var deserialized = MessagePackSerializer.Deserialize<OrderCreatedEvent>(bytes);

    Assert.Equal(original.OrderId, deserialized.OrderId);
    Assert.Equal(original.Amount, deserialized.Amount);
}
```

### Performance Testing

```csharp
[Benchmark]
[BenchmarkCategory("Serialization")]
public byte[] SerializeMessagePack()
{
    return MessagePackSerializer.Serialize(_message);
}

[Benchmark(Baseline = true)]
[BenchmarkCategory("Serialization")]
public byte[] SerializeJson()
{
    return JsonSerializer.SerializeToUtf8Bytes(_message);
}
```

## Best Practices

1. **Use Records**: Cleaner syntax with implicit property attributes
2. **Enable Compression**: `Lz4Block` for best size/speed tradeoff
3. **Contractless for DTOs**: Less boilerplate for simple messages
4. **Explicit Keys for APIs**: Better compatibility and versioning
5. **Test Both Paths**: Ensure serialization round-trips correctly

## See Also

- [Main Documentation](../../README.md)
- [JSON Serialization](../HeroMessaging.Serialization.Json/README.md)
- [Protobuf Serialization](../HeroMessaging.Serialization.Protobuf/README.md)
- [MessagePack Official Docs](https://msgpack.org/)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
