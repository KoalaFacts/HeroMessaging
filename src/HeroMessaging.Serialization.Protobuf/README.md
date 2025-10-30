# HeroMessaging.Serialization.Protobuf

**Protocol Buffers serialization for HeroMessaging with schema evolution and cross-language support.**

## Overview

Protocol Buffers (Protobuf) is Google's language-neutral, platform-neutral serialization format. This provider offers excellent performance with strong schema evolution guarantees, making it ideal for long-lived systems and multi-language environments.

**Key Benefits**:
- **Schema Evolution**: Add/remove fields without breaking compatibility
- **Cross-Language**: Generate code for C++, Java, Python, Go, and more
- **Compact**: Binary format smaller than JSON
- **Fast**: Comparable to MessagePack performance
- **Strongly Typed**: Contract-first design with .proto files

## Installation

```bash
dotnet add package HeroMessaging.Serialization.Protobuf
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

**Note**: Not available for .NET Standard 2.0

## Quick Start

### 1. Define .proto Schema

Create `messages.proto`:

```protobuf
syntax = "proto3";

package heromessaging.messages;

message OrderCreatedEvent {
  string message_id = 1;
  int64 timestamp = 2;
  string order_id = 3;
  double amount = 4;
  string customer_id = 5;
}
```

### 2. Generate C# Code

```bash
# Install protoc compiler
dotnet tool install --global protobuf-net.Protogen

# Generate C# classes
protogen --csharp_out=. messages.proto
```

### 3. Configure HeroMessaging

```csharp
using HeroMessaging;
using HeroMessaging.Serialization.Protobuf;

services.AddHeroMessaging(builder =>
{
    builder.UseProtobufSerialization();
});
```

## Using protobuf-net (Code-First)

### Attribute-Based Approach

```csharp
using ProtoBuf;

[ProtoContract]
public class OrderCreatedEvent : IEvent
{
    [ProtoMember(1)]
    public Guid MessageId { get; set; }

    [ProtoMember(2)]
    public DateTime Timestamp { get; set; }

    [ProtoMember(3)]
    public string OrderId { get; set; } = string.Empty;

    [ProtoMember(4)]
    public decimal Amount { get; set; }

    [ProtoMember(5)]
    public string CustomerId { get; set; } = string.Empty;
}
```

### Records (Recommended)

```csharp
[ProtoContract]
public record OrderCreatedEvent(
    [property: ProtoMember(1)] Guid MessageId,
    [property: ProtoMember(2)] DateTime Timestamp,
    [property: ProtoMember(3)] string OrderId,
    [property: ProtoMember(4)] decimal Amount,
    [property: ProtoMember(5)] string CustomerId
) : IEvent;
```

## Schema Evolution

### Adding Fields (Backward Compatible)

```csharp
[ProtoContract]
public class OrderCreatedEvent : IEvent
{
    [ProtoMember(1)]
    public Guid MessageId { get; set; }

    [ProtoMember(2)]
    public DateTime Timestamp { get; set; }

    [ProtoMember(3)]
    public string OrderId { get; set; } = string.Empty;

    [ProtoMember(4)]
    public decimal Amount { get; set; }

    // ✅ NEW FIELD - Safe to add
    [ProtoMember(5)]
    public string? CustomerEmail { get; set; }

    // ✅ OPTIONAL - Safe to add
    [ProtoMember(6)]
    public DateTime? ShippingDate { get; set; }
}
```

**Rules**:
- Always use new field numbers
- Old readers ignore unknown fields
- New readers use default values for missing fields

### Removing Fields (Backward Compatible)

```csharp
[ProtoContract]
public class OrderCreatedEvent : IEvent
{
    [ProtoMember(1)]
    public Guid MessageId { get; set; }

    [ProtoMember(2)]
    public DateTime Timestamp { get; set; }

    [ProtoMember(3)]
    public string OrderId { get; set; } = string.Empty;

    // ❌ REMOVED - Field 4 reserved
    // [ProtoMember(4)]
    // public decimal LegacyField { get; set; }

    [ProtoMember(5)]
    public decimal Amount { get; set; }
}
```

**Rules**:
- Don't reuse field numbers
- Reserve removed field numbers in comments
- Old readers safely ignore missing fields

### Renaming Fields (Safe)

```csharp
// Field numbers matter, not names!
[ProtoMember(3)]
public string OrderId { get; set; } // Was: order_id

// ✅ Safe - field number unchanged
[ProtoMember(3)]
public string OrderIdentifier { get; set; }
```

## Performance

**Benchmarks** (compared to JSON and MessagePack):

| Metric | Protobuf | MessagePack | JSON |
|--------|----------|-------------|------|
| Serialization | ~1.5 μs | ~1.2 μs | ~4.5 μs |
| Deserialization | ~2.0 μs | ~1.8 μs | ~5.2 μs |
| Payload Size | ~160 bytes | ~180 bytes | ~420 bytes |
| Schema Evolution | ✅ Excellent | ⚠️ Manual | ⚠️ Manual |

**When Protobuf Wins**:
- Long-lived systems needing schema evolution
- Multi-language environments
- Contract-first API design
- Smallest possible payload size

## Configuration

### RuntimeTypeModel Configuration

```csharp
using ProtoBuf.Meta;

services.AddHeroMessaging(builder =>
{
    builder.UseProtobufSerialization(options =>
    {
        // Configure protobuf-net runtime
        options.Model = RuntimeTypeModel.Create();
        options.Model.Add(typeof(OrderCreatedEvent), true);

        // Compilation for better performance
        options.Model.CompileInPlace();
    });
});
```

### Advanced Options

```csharp
builder.UseProtobufSerialization(options =>
{
    // Use packed encoding (smaller for repeated fields)
    options.Model.UseImplicitZeroDefaults = false;

    // Skip default values
    options.Model.AutoAddMissingTypes = true;

    // Performance: compile model once
    var compiled = options.Model.Compile();
});
```

## Cross-Language Scenarios

### Generate Code for Other Languages

**Python**:
```bash
protoc --python_out=. messages.proto
```

**Java**:
```bash
protoc --java_out=. messages.proto
```

**Go**:
```bash
protoc --go_out=. messages.proto
```

### Interop Example

**C# Producer**:
```csharp
var @event = new OrderCreatedEvent
{
    MessageId = Guid.NewGuid(),
    OrderId = "ORD-001",
    Amount = 99.99m
};

await messaging.Publish(@event);
// Serialized as Protobuf binary
```

**Python Consumer**:
```python
import messages_pb2

# Deserialize Protobuf binary from queue
event = messages_pb2.OrderCreatedEvent()
event.ParseFromString(message_bytes)

print(f"Order ID: {event.order_id}")
print(f"Amount: {event.amount}")
```

## When to Use Protobuf

### ✅ Best For

1. **Microservices with Schema Evolution**
   - Long-lived systems
   - Gradual deployments
   - Need backward/forward compatibility

2. **Multi-Language Systems**
   - C# backend, Python ML services
   - Java legacy systems
   - Go microservices

3. **Contract-First Design**
   - API contracts in .proto files
   - Code generation from schemas
   - Shared contracts across teams

4. **Mobile/IoT Scenarios**
   - Bandwidth-constrained environments
   - Need smallest payloads
   - Battery efficiency matters

### ❌ Avoid When

1. **HTTP APIs** - Use JSON for better REST compatibility
2. **Simple Internal Services** - MessagePack is faster without schemas
3. **Rapid Prototyping** - JSON is easier to debug
4. **Dynamic Schemas** - Protobuf requires predefined schemas

## Troubleshooting

### Common Issues

#### Issue: "No serializer defined" Error

**Symptoms**:
```
InvalidOperationException: No serializer defined for type OrderCreatedEvent
```

**Solution**:
```csharp
// Add [ProtoContract] and [ProtoMember] attributes
[ProtoContract]
public class OrderCreatedEvent
{
    [ProtoMember(1)] // Required!
    public Guid MessageId { get; set; }
}

// Or manually add to model
options.Model.Add(typeof(OrderCreatedEvent), true);
```

#### Issue: Field Numbers Conflict

**Symptoms**:
```
InvalidOperationException: Duplicate field number
```

**Solution**:
```csharp
// Ensure unique field numbers
[ProtoMember(1)] // ✅ Unique
[ProtoMember(2)] // ✅ Unique
[ProtoMember(2)] // ❌ Duplicate!
```

#### Issue: Incompatible with MessagePack

**Symptoms**:
- Mixed serialization errors
- "Invalid message format"

**Solution**:
- Don't mix Protobuf and MessagePack in same queue
- Use content-type headers to distinguish formats
- Or configure per-queue serialization

### Debugging

View binary output:

```csharp
using ProtoBuf;

var @event = new OrderCreatedEvent { OrderId = "ORD-001" };
using var ms = new MemoryStream();
Serializer.Serialize(ms, @event);

var bytes = ms.ToArray();
var hex = BitConverter.ToString(bytes);
Console.WriteLine($"Protobuf hex: {hex}");

// Use protoc to decode:
// echo <hex> | xxd -r -p | protoc --decode=OrderCreatedEvent messages.proto
```

## Best Practices

1. **Version Your Schemas**: Keep .proto files in version control
2. **Never Reuse Field Numbers**: Reserve removed numbers
3. **Use Required Sparingly**: Optional fields allow evolution
4. **Document Breaking Changes**: Track incompatible schema updates
5. **Test Cross-Language**: If using multiple languages, test interop
6. **Compile Models**: `CompileInPlace()` for better performance

## Advanced Features

### Nested Messages

```csharp
[ProtoContract]
public class OrderCreatedEvent
{
    [ProtoMember(1)]
    public string OrderId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public Customer Customer { get; set; } = new();

    [ProtoMember(3)]
    public List<OrderItem> Items { get; set; } = new();
}

[ProtoContract]
public class Customer
{
    [ProtoMember(1)]
    public string CustomerId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Email { get; set; } = string.Empty;
}

[ProtoContract]
public class OrderItem
{
    [ProtoMember(1)]
    public string ProductId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Quantity { get; set; }
}
```

### Enum Support

```csharp
[ProtoContract]
public enum OrderStatus
{
    [ProtoEnum]
    Unknown = 0,

    [ProtoEnum]
    Pending = 1,

    [ProtoEnum]
    Confirmed = 2,

    [ProtoEnum]
    Shipped = 3
}
```

### Oneof (Union Types)

```csharp
[ProtoContract]
public class PaymentMethod
{
    [ProtoMember(1)]
    public CreditCard? CreditCard { get; set; }

    [ProtoMember(2)]
    public BankTransfer? BankTransfer { get; set; }

    // Only one will be set
}
```

## See Also

- [Main Documentation](../../README.md)
- [JSON Serialization](../HeroMessaging.Serialization.Json/README.md)
- [MessagePack Serialization](../HeroMessaging.Serialization.MessagePack/README.md)
- [Protocol Buffers Official Docs](https://protobuf.dev/)
- [protobuf-net Documentation](https://protobuf-net.github.io/protobuf-net/)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
