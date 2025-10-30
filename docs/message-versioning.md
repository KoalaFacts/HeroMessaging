# Message Versioning Guide

**Strategies for evolving message schemas in HeroMessaging without breaking compatibility.**

## Overview

Message versioning allows you to change message schemas over time while maintaining backward and forward compatibility. HeroMessaging provides built-in converters and a conversion path resolver to handle schema evolution automatically.

## When to Version Messages

Version messages when:
- ✅ Adding new fields to existing messages
- ✅ Removing deprecated fields
- ✅ Renaming fields for clarity
- ✅ Changing field types (with conversion)
- ✅ Splitting/merging messages

Don't version for:
- ❌ Fixing bugs in message handling
- ❌ Internal implementation changes
- ❌ Performance improvements

## Versioning Strategies

### 1. Attribute-Based Versioning (Recommended)

```csharp
using HeroMessaging.Versioning;

[MessageVersion("1.0.0")]
public record OrderCreatedEvent_V1(
    string OrderId,
    decimal Amount
) : IEvent;

[MessageVersion("2.0.0")]
public record OrderCreatedEvent_V2(
    string OrderId,
    decimal Amount,
    string CustomerId  // New field
) : IEvent;
```

### 2. Property-Based Versioning

```csharp
public record OrderCreatedEvent : IEvent, IVersionedMessage
{
    public Version MessageVersion => new Version("2.0.0");
    
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? CustomerId { get; init; }  // Optional for v1 compatibility
}
```

## Built-In Converters

### Add Property Converter

Handles adding new fields:

```csharp
services.AddHeroMessaging(builder =>
{
    builder.UseVersioning(versioning =>
    {
        versioning.RegisterConverter(new AddPropertyConverter<OrderCreatedEvent>(
            fromVersion: "1.0.0",
            toVersion: "2.0.0",
            propertyName: "CustomerId",
            defaultValue: "UNKNOWN"
        ));
    });
});
```

### Remove Property Converter

Handles field removal:

```csharp
versioning.RegisterConverter(new RemovePropertyConverter<OrderCreatedEvent>(
    fromVersion: "2.0.0",
    toVersion: "3.0.0",
    propertyName: "LegacyField"
));
```

### Rename Property Converter

Handles field renaming:

```csharp
versioning.RegisterConverter(new RenamePropertyConverter<OrderCreatedEvent>(
    fromVersion: "1.0.0",
    toVersion: "2.0.0",
    oldName: "OrderNumber",
    newName: "OrderId"
));
```

### Transform Converter

Custom transformations:

```csharp
versioning.RegisterConverter(new TransformConverter<OrderCreatedEvent>(
    fromVersion: "1.0.0",
    toVersion: "2.0.0",
    transform: (oldMessage, newMessage) =>
    {
        // Convert decimal to cents (int)
        newMessage.SetProperty("AmountInCents", 
            (int)(oldMessage.GetProperty<decimal>("Amount") * 100));
    }
));
```

## Custom Converters

Create complex conversion logic:

```csharp
public class OrderEventV1ToV2Converter : IMessageConverter<OrderCreatedEvent>
{
    public Version FromVersion => new Version("1.0.0");
    public Version ToVersion => new Version("2.0.0");

    public object Convert(object message)
    {
        var v1 = (OrderCreatedEvent_V1)message;

        return new OrderCreatedEvent_V2(
            OrderId: v1.OrderId,
            Amount: v1.Amount,
            CustomerId: "MIGRATED",  // Default for missing field
            CreatedAt: DateTime.UtcNow  // Add timestamp
        );
    }
}

// Register
versioning.RegisterConverter(new OrderEventV1ToV2Converter());
```

## Conversion Paths

HeroMessaging automatically finds conversion paths:

```
v1.0.0 ─[Converter A]→ v2.0.0 ─[Converter B]→ v3.0.0
```

Multi-hop conversion is automatic:

```csharp
// You have: v1.0.0 message
// You want: v3.0.0 handler

// HeroMessaging automatically:
// 1. Applies Converter A (v1 → v2)
// 2. Applies Converter B (v2 → v3)
// 3. Delivers to handler
```

## Semantic Versioning

Follow SemVer for message versions:

**MAJOR.MINOR.PATCH**

- **MAJOR**: Breaking changes (field type change, required field removed)
- **MINOR**: Backward-compatible additions (new optional field)
- **PATCH**: Bug fixes (no schema change)

Examples:
- `1.0.0` → `1.1.0`: Added optional field ✅
- `1.0.0` → `2.0.0`: Changed field type ⚠️
- `1.0.0` → `1.0.1`: Fixed validation ✅

## Testing Versioned Messages

```csharp
[Fact]
public void CanConvertV1ToV2()
{
    var v1 = new OrderCreatedEvent_V1("ORD-001", 99.99m);
    
    var converter = new OrderEventV1ToV2Converter();
    var v2 = (OrderCreatedEvent_V2)converter.Convert(v1);
    
    Assert.Equal("ORD-001", v2.OrderId);
    Assert.Equal(99.99m, v2.Amount);
    Assert.NotNull(v2.CustomerId);
}

[Fact]
public async Task HandlerReceivesConvertedMessage()
{
    // Publish v1
    await messaging.Publish(new OrderCreatedEvent_V1("ORD-001", 99.99m));
    
    // Handler expects v2 - automatic conversion
    var handler = Mock.Of<IEventHandler<OrderCreatedEvent_V2>>();
    
    // Verify v2 handler received converted message
    Mock.Get(handler).Verify(h => 
        h.Handle(It.Is<OrderCreatedEvent_V2>(e => e.CustomerId != null), 
        It.IsAny<CancellationToken>()), 
        Times.Once);
}
```

## Best Practices

1. **Version from Day 1**: Start with v1.0.0 even if you don't need versioning yet
2. **Additive Changes**: Prefer adding optional fields over removing required ones
3. **Test Conversions**: Always test old→new and new→old paths
4. **Document Migrations**: Keep changelog of schema changes
5. **Gradual Rollout**: Deploy converters before new message versions
6. **Monitor Conversions**: Log when conversions occur in production

## Migration Strategies

### Strategy 1: Dual Publishing

Support both versions during migration:

```csharp
// Publisher sends both v1 and v2
await messaging.Publish(new OrderCreatedEvent_V1(...));
await messaging.Publish(new OrderCreatedEvent_V2(...));

// Consumers choose their version
// Old consumers: v1 handler
// New consumers: v2 handler
```

### Strategy 2: Converter-Based Migration

Use converters for transparent upgrade:

```csharp
// Publishers stay on v1
await messaging.Publish(new OrderCreatedEvent_V1(...));

// Converters upgrade to v2 automatically
// New consumers receive v2 via conversion
```

### Strategy 3: Event Forwarding

Republish events in new format:

```csharp
public class OrderEventMigrator : IEventHandler<OrderCreatedEvent_V1>
{
    public async Task Handle(OrderCreatedEvent_V1 v1Event, CancellationToken ct)
    {
        // Convert and republish
        var v2Event = ConvertToV2(v1Event);
        await messaging.Publish(v2Event);
    }
}
```

## Troubleshooting

### Conversion Not Working

Check converter registration:

```csharp
// Enable conversion logging
services.AddLogging(builder =>
{
    builder.AddFilter("HeroMessaging.Versioning", LogLevel.Debug);
});
```

### No Conversion Path Found

Ensure all intermediate converters exist:

```csharp
// v1 → v2 (missing!)
// v2 → v3 (exists)

// Fix: Add v1 → v2 converter
versioning.RegisterConverter(new V1ToV2Converter());
```

### Infinite Conversion Loop

Avoid circular conversions:

```csharp
// ❌ BAD: Circular conversion
v1 → v2 → v1  // Infinite loop!

// ✅ GOOD: Linear progression
v1 → v2 → v3  // Clear path
```

## See Also

- [Main Documentation](../README.md)
- [Saga Orchestration](orchestration-pattern.md)
- [Architecture Decision Records](adr/)

---

**Last Updated**: 2025-10-30
**Version**: 1.0.0
