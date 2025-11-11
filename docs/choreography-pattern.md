# Choreography Pattern Guide

## Overview

The **choreography pattern** is a decentralized approach to coordinating workflows where services react to events and publish new events, without a central orchestrator. Each service is autonomous and knows how to react to events it cares about.

HeroMessaging Phase 1 provides full support for choreography through:
- **Correlation tracking** - Link all messages in a workflow
- **Causation tracking** - Track which message caused which
- **Ambient context** - Automatic propagation through async operations
- **Zero-configuration** - Works out of the box with event handlers

---

## Key Concepts

### Correlation ID
Links all messages that belong to the same workflow or business transaction.

**Example:** An order processing workflow where `OrderCreated`, `InventoryReserved`, `PaymentProcessed`, and `OrderShipped` events all share the same `CorrelationId`.

### Causation ID
Identifies which specific message directly caused the current message. Forms a chain of causality.

**Example:**
```
OrderCreated (MessageId: A)
  → InventoryReserved (CausationId: A, MessageId: B)
    → PaymentProcessed (CausationId: B, MessageId: C)
      → OrderShipped (CausationId: C, MessageId: D)
```

### Ambient Context
Uses `AsyncLocal<T>` to flow correlation information through async operations without explicit parameter passing.

---

## Quick Start

### 1. Define Messages Using MessageBase

```csharp
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;

// Use MessageBase for automatic correlation support
public record OrderCreatedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal TotalAmount { get; init; }
}

public record InventoryReservedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid ReservationId { get; init; }
}

public record PaymentProcessedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid TransactionId { get; init; }
}
```

### 2. Create Event Handlers

```csharp
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Choreography;

public class ReserveInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IHeroMessaging _messaging;
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(IHeroMessaging messaging, ILogger<ReserveInventoryHandler> logger)
    {
        _messaging = messaging;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reserving inventory for order {OrderId} in correlation {CorrelationId}",
            @event.OrderId, @event.CorrelationId);

        // Perform business logic
        var reservationId = await ReserveInventoryAsync(@event.OrderId);

        // Publish next event with automatic correlation
        var nextEvent = new InventoryReservedEvent
        {
            OrderId = @event.OrderId,
            ReservationId = reservationId
        }.WithCorrelation(); // ← Automatically applies CorrelationId and CausationId

        await _messaging.Publish(nextEvent, cancellationToken);
    }

    private async Task<Guid> ReserveInventoryAsync(Guid orderId)
    {
        // Your inventory reservation logic
        await Task.Delay(10); // Simulate work
        return Guid.NewGuid();
    }
}
```

### 3. Configure HeroMessaging

```csharp
services.AddHeroMessaging(builder => builder
    .WithEventBus()      // Enable event publishing
    .ScanAssembly(typeof(Program).Assembly)); // Auto-register handlers

// UseCorrelation() is added by default in EventBus
```

### 4. Start a Workflow

```csharp
var messaging = serviceProvider.GetRequiredService<IHeroMessaging>();

var orderEvent = new OrderCreatedEvent
{
    OrderId = Guid.NewGuid(),
    CustomerId = Guid.NewGuid(),
    TotalAmount = 99.99m,
    CorrelationId = Guid.NewGuid().ToString() // Optional: start new correlation
};

await messaging.Publish(orderEvent);

// All subsequent events will automatically inherit this CorrelationId
// and set their CausationId to the MessageId of the event that triggered them
```

---

## Complete Example: Order Processing Workflow

### Workflow Steps

```
1. OrderCreatedEvent
   ↓ (triggers)
2. ReserveInventoryHandler → publishes InventoryReservedEvent
   ↓ (triggers)
3. ProcessPaymentHandler → publishes PaymentProcessedEvent
   ↓ (triggers)
4. ShipOrderHandler → publishes OrderShippedEvent
   ↓ (complete)
```

### Messages

```csharp
public record OrderCreatedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal TotalAmount { get; init; }
}

public record InventoryReservedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid ReservationId { get; init; }
}

public record PaymentProcessedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid TransactionId { get; init; }
    public decimal Amount { get; init; }
}

public record OrderShippedEvent : MessageBase, IEvent
{
    public Guid OrderId { get; init; }
    public Guid ShipmentId { get; init; }
    public string TrackingNumber { get; init; }
}
```

### Handlers

```csharp
// Step 1: Reserve Inventory
public class ReserveInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IHeroMessaging _messaging;
    private readonly IInventoryService _inventory;

    public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        var reservationId = await _inventory.ReserveAsync(@event.OrderId);

        await _messaging.Publish(new InventoryReservedEvent
        {
            OrderId = @event.OrderId,
            ReservationId = reservationId
        }.WithCorrelation(), cancellationToken);
    }
}

// Step 2: Process Payment
public class ProcessPaymentHandler : IEventHandler<InventoryReservedEvent>
{
    private readonly IHeroMessaging _messaging;
    private readonly IPaymentService _payment;

    public async Task Handle(InventoryReservedEvent @event, CancellationToken cancellationToken)
    {
        var transactionId = await _payment.ProcessAsync(@event.OrderId);

        await _messaging.Publish(new PaymentProcessedEvent
        {
            OrderId = @event.OrderId,
            TransactionId = transactionId,
            Amount = 99.99m
        }.WithCorrelation(), cancellationToken);
    }
}

// Step 3: Ship Order
public class ShipOrderHandler : IEventHandler<PaymentProcessedEvent>
{
    private readonly IHeroMessaging _messaging;
    private readonly IShippingService _shipping;

    public async Task Handle(PaymentProcessedEvent @event, CancellationToken cancellationToken)
    {
        var shipmentId = await _shipping.CreateShipmentAsync(@event.OrderId);

        await _messaging.Publish(new OrderShippedEvent
        {
            OrderId = @event.OrderId,
            ShipmentId = shipmentId,
            TrackingNumber = $"TRACK-{shipmentId:N}"
        }.WithCorrelation(), cancellationToken);
    }
}
```

---

## Advanced Usage

### Manual Correlation Control

```csharp
// Manually set correlation IDs
var message = new OrderCreatedEvent
{
    OrderId = Guid.NewGuid()
}.WithCorrelation(
    correlationId: "my-workflow-123",
    causationId: "previous-message-456"
);
```

### Querying Correlation Information

```csharp
public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
{
    // Check if message has correlation
    if (@event.HasCorrelation())
    {
        _logger.LogInformation("Processing correlated message: {Chain}",
            @event.GetCorrelationChain());

        // Output: "Correlation=abc → Causation=def → Message=xyz"
    }

    // Extract correlation tuple
    var (correlationId, causationId) = @event.GetCorrelation();
}
```

### Accessing Current Correlation Context

```csharp
using HeroMessaging.Choreography;

public class MyService
{
    public async Task DoWorkAsync()
    {
        // Access current correlation context from anywhere
        var correlationId = CorrelationContext.CurrentCorrelationId;
        var messageId = CorrelationContext.CurrentMessageId;

        if (correlationId != null)
        {
            _logger.LogInformation("Working in correlation {CorrelationId}", correlationId);
        }

        // Context flows through async operations automatically
        await Task.Delay(100);
        // correlationId still accessible here
    }
}
```

### Cross-Service Correlation

When publishing to external systems (e.g., via RabbitMQ), correlation metadata flows automatically through `TransportEnvelope`:

```csharp
// In handler:
await _messaging.Publish(new OrderCreatedEvent { ... }.WithCorrelation());

// Internally, HeroMessaging creates TransportEnvelope with:
var envelope = new TransportEnvelope
{
    CorrelationId = message.CorrelationId,
    CausationId = message.CausationId,
    // ... other properties
};
```

---

## Observability

### Logging with Correlation

```csharp
using Microsoft.Extensions.Logging;

public class MyHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<MyHandler> _logger;

    public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Log with correlation context
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = @event.CorrelationId,
            ["CausationId"] = @event.CausationId,
            ["MessageId"] = @event.MessageId
        }))
        {
            _logger.LogInformation("Processing order {@Event}", @event);

            // All logs within this scope will include correlation information
        }
    }
}
```

### Distributed Tracing

Correlation and causation IDs integrate seamlessly with OpenTelemetry:

```csharp
services.AddHeroMessaging(builder => builder
    .WithEventBus()
    .WithOpenTelemetry()); // Automatically adds trace context

// CorrelationId → maps to TraceId
// CausationId → maps to ParentSpanId
// MessageId → maps to SpanId
```

---

## Best Practices

### ✅ DO

1. **Always use `.WithCorrelation()`** when publishing events from handlers
2. **Use `MessageBase`** as the base for your messages for automatic support
3. **Start workflows with a CorrelationId** to ensure all messages are linked
4. **Log correlation information** for debugging and tracing
5. **Keep handlers focused** - one handler per event type per service
6. **Handle failures gracefully** - use error handlers and dead letter queues

### ❌ DON'T

1. **Don't create circular dependencies** - Event A triggers B triggers A (infinite loop)
2. **Don't forget correlation** - Always call `.WithCorrelation()` on outgoing messages
3. **Don't rely on order** - Choreography is asynchronous; don't assume message ordering
4. **Don't put business logic in constructors** - Keep messages as simple DTOs
5. **Don't mutate messages** - Messages are immutable records; use `with` expressions
6. **Don't couple services tightly** - Each service should only know about events, not other services

---

## Troubleshooting

### Correlation Not Propagating

**Problem:** New events don't have CorrelationId from parent message

**Solution:** Ensure you're calling `.WithCorrelation()`:
```csharp
// ❌ Wrong
await _messaging.Publish(new MyEvent { ... });

// ✅ Correct
await _messaging.Publish(new MyEvent { ... }.WithCorrelation());
```

### Missing Causation Chain

**Problem:** CausationId is null in child events

**Solution:** Make sure `UseCorrelation()` is in your pipeline:
```csharp
builder
    .UseMetrics()
    .UseLogging()
    .UseCorrelation()  // ← Must be present
    .UseValidation()
    .UseErrorHandling();
```

### Correlation Lost Across Async Boundaries

**Problem:** CorrelationContext is null after `await`

**Solution:** Ensure you're using `async/await` correctly and not mixing `Task.Run` or `Task.Factory.StartNew` without proper context flow.

---

## Comparison: Choreography vs Orchestration

| Aspect | Choreography (Phase 1) | Orchestration (Phase 2) |
|--------|------------------------|-------------------------|
| **Control** | Decentralized | Centralized |
| **Coordination** | Events | Commands from orchestrator |
| **State** | Implicit (in services) | Explicit (in saga) |
| **Complexity** | Distributed | Centralized |
| **Best For** | Simple workflows, cross-context | Complex workflows, compensations |

**When to Use Choreography:**
- 3-5 step workflows
- Services in different bounded contexts
- Independent teams
- High scalability needs

**When to Use Orchestration (Coming in Phase 2):**
- 5+ step workflows with complex branching
- Need for centralized monitoring
- Compensation transactions required
- Timeouts and deadlines critical

---

## Examples

See [`tests/HeroMessaging.Tests/Integration/ChoreographyWorkflowTests.cs`](../tests/HeroMessaging.Tests/Integration/ChoreographyWorkflowTests.cs) for a complete working example of an order processing workflow using choreography.

---

## Next Steps

- **Phase 2: Orchestration** - Learn about saga pattern and process managers (coming soon)
- **OpenTelemetry Integration** - See how correlation integrates with distributed tracing
- **Error Handling** - Configure dead letter queues and retry policies
- **Testing Workflows** - Write integration tests for choreography patterns

---

## API Reference

### MessageBase

```csharp
public abstract record MessageBase : IMessage
{
    public Guid MessageId { get; init; }
    public DateTime Timestamp { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

### CorrelationContext

```csharp
public static class CorrelationContext
{
    public static CorrelationState? Current { get; }
    public static string? CurrentCorrelationId { get; }
    public static string? CurrentMessageId { get; }
    public static IDisposable BeginScope(string? correlationId, string messageId);
    public static IDisposable BeginScope(IMessage message);
}
```

### Extension Methods

```csharp
public static class MessageCorrelationExtensions
{
    public static TMessage WithCorrelation<TMessage>(this TMessage message) where TMessage : MessageBase;
    public static TMessage WithCorrelation<TMessage>(this TMessage message, string correlationId, string? causationId = null) where TMessage : MessageBase;
    public static (string? CorrelationId, string? CausationId) GetCorrelation(this IMessage message);
    public static bool HasCorrelation(this IMessage message);
    public static bool HasCausation(this IMessage message);
    public static string GetCorrelationChain(this IMessage message);
}
```

---

**Generated:** 2025-10-27
**Version:** Phase 1 - Choreography Support
**Next:** Phase 2 - Orchestration with Saga Pattern
