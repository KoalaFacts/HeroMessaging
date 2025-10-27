# Enhanced State Machine Builder API

**Version**: 2.0
**Status**: Stable
**Last Updated**: 2025-10-27

## Overview

The enhanced builder API provides a more intuitive and powerful way to define state machines for saga orchestration. Building on the solid foundation of the original builder, these extensions add convenience methods, conditional logic, and improved fluency.

## Key Improvements

### 1. Inline State Definition
**Before:**
```csharp
var awaitingPayment = new State("AwaitingPayment");
builder.During(awaitingPayment)
    .When(paymentReceived)
    .TransitionTo(completed);
```

**After:**
```csharp
builder.InState("AwaitingPayment")
    .When(paymentReceived)
    .TransitionTo("Completed");
```

### 2. Conditional Transitions
**Before:**
```csharp
builder.During(processing)
    .When(dataReceived)
    .Then(ctx =>
    {
        if (ctx.Data.Amount > 1000)
        {
            ctx.Instance.RequiresApproval = true;
            ctx.Instance.TransitionTo("AwaitingApproval");
        }
        else
        {
            ctx.Instance.TransitionTo("Completed");
        }
        return Task.CompletedTask;
    });
```

**After:**
```csharp
builder.InState("Processing")
    .When(dataReceived)
    .If(ctx => ctx.Data.Amount > 1000)
        .Then(ctx => ctx.Instance.RequiresApproval = true)
        .TransitionTo("AwaitingApproval")
    .Else()
        .TransitionTo("Completed")
    .EndIf();
```

### 3. Data Mapping
**Before:**
```csharp
builder.Initially()
    .When(orderCreated)
    .Then(ctx =>
    {
        ctx.Instance.OrderId = ctx.Data.OrderId;
        ctx.Instance.CustomerId = ctx.Data.CustomerId;
        ctx.Instance.TotalAmount = ctx.Data.TotalAmount;
        return Task.CompletedTask;
    });
```

**After:**
```csharp
builder.Initially()
    .When(orderCreated)
    .CopyFrom((saga, evt) =>
    {
        saga.OrderId = evt.OrderId;
        saga.CustomerId = evt.CustomerId;
        saga.TotalAmount = evt.TotalAmount;
    });
```

### 4. Property Setters
**Before:**
```csharp
builder.During(state)
    .When(event)
    .Then(ctx =>
    {
        ctx.Instance.PaymentId = ctx.Data.PaymentId;
        return Task.CompletedTask;
    });
```

**After:**
```csharp
builder.InState("AwaitingPayment")
    .When(paymentReceived)
    .SetProperty(
        (saga, id) => saga.PaymentId = id,
        ctx => ctx.Data.PaymentId);
```

### 5. Compensation
**Before:**
```csharp
builder.During(state)
    .When(event)
    .Then(ctx =>
    {
        ctx.Compensation.AddCompensation(
            "RefundPayment",
            async ct => { /* refund logic */ });
        return Task.CompletedTask;
    });
```

**After:**
```csharp
builder.InState("AwaitingPayment")
    .When(paymentReceived)
    .CompensateWith("RefundPayment", async ct =>
    {
        // Refund logic
    });
```

### 6. Multiple Actions
**Before:**
```csharp
builder.During(state)
    .When(event)
    .Then(async ctx =>
    {
        await Action1(ctx);
        await Action2(ctx);
        await Action3(ctx);
    });
```

**After:**
```csharp
builder.InState("Processing")
    .When(event)
    .ThenAll(
        ctx => Action1(ctx),
        ctx => Action2(ctx),
        ctx => Action3(ctx));
```

## Complete Example

### Traditional API
```csharp
public static StateMachineDefinition<OrderSaga> BuildTraditional()
{
    var builder = new StateMachineBuilder<OrderSaga>();

    // Pre-declare states
    var awaitingPayment = new State("AwaitingPayment");
    var awaitingInventory = new State("AwaitingInventory");
    var completed = new State("Completed");
    var failed = new State("Failed");

    // Pre-declare events
    var orderCreated = new Event<OrderCreatedEvent>("OrderCreated");
    var paymentReceived = new Event<PaymentReceivedEvent>("PaymentReceived");
    var inventoryReserved = new Event<InventoryReservedEvent>("InventoryReserved");

    builder.Initially()
        .When(orderCreated)
        .Then(ctx =>
        {
            ctx.Instance.OrderId = ctx.Data.OrderId;
            ctx.Instance.TotalAmount = ctx.Data.TotalAmount;
            return Task.CompletedTask;
        })
        .TransitionTo(awaitingPayment);

    builder.During(awaitingPayment)
        .When(paymentReceived)
        .Then(ctx =>
        {
            ctx.Instance.PaymentId = ctx.Data.PaymentId;
            ctx.Compensation.AddCompensation(
                "RefundPayment",
                async ct => { /* refund */ });
            return Task.CompletedTask;
        })
        .TransitionTo(awaitingInventory);

    builder.During(awaitingInventory)
        .When(inventoryReserved)
        .Then(ctx =>
        {
            ctx.Instance.ReservationId = ctx.Data.ReservationId;
            return Task.CompletedTask;
        })
        .TransitionTo(completed)
        .Finalize();

    return builder.Build();
}
```

### Enhanced API
```csharp
public static StateMachineDefinition<OrderSaga> BuildEnhanced()
{
    var builder = new StateMachineBuilder<OrderSaga>();

    builder.Initially()
        .When(new Event<OrderCreatedEvent>("OrderCreated"))
        .CopyFrom((saga, evt) =>
        {
            saga.OrderId = evt.OrderId;
            saga.TotalAmount = evt.TotalAmount;
        })
        .TransitionTo("AwaitingPayment");

    builder.InState("AwaitingPayment")
        .When(new Event<PaymentReceivedEvent>("PaymentReceived"))
        .SetProperty(
            (saga, id) => saga.PaymentId = id,
            ctx => ctx.Data.PaymentId)
        .CompensateWith("RefundPayment", async ct =>
        {
            // Refund logic
        })
        .TransitionTo("AwaitingInventory");

    builder.InState("AwaitingInventory")
        .When(new Event<InventoryReservedEvent>("InventoryReserved"))
        .SetProperty(
            (saga, id) => saga.ReservationId = id,
            ctx => ctx.Data.ReservationId)
        .TransitionTo("Completed")
        .MarkAsCompleted();

    return builder.Build();
}
```

## Advanced Patterns

### Nested Conditionals
```csharp
builder.InState("Processing")
    .When(dataReceived)
    .If(ctx => ctx.Data.Amount > 10000)
        .Then(ctx => { /* High value processing */ })
        .TransitionTo("HighValueReview")
    .Else()
        .If(ctx => ctx.Instance.IsPremiumCustomer)
            .Then(ctx => { /* Premium fast-track */ })
            .TransitionTo("FastTrack")
        .Else()
            .Then(ctx => { /* Standard processing */ })
            .TransitionTo("StandardProcessing")
        .EndIf()
    .EndIf();
```

### Complex Workflows
```csharp
builder.Initially()
    .When(new Event<OrderCreatedEvent>("OrderCreated"))
    // Copy basic order data
    .CopyFrom((saga, evt) =>
    {
        saga.OrderId = evt.OrderId;
        saga.CustomerId = evt.CustomerId;
    })
    // Apply business rules
    .If(ctx => ctx.Data.TotalAmount > 1000)
        .SetProperty(
            (saga, val) => saga.RequiresManagerApproval = val,
            ctx => true)
    .EndIf()
    // Set up compensation
    .CompensateWith("CancelOrder", async ct =>
    {
        // Cancellation logic
    })
    // Execute multiple actions
    .ThenAll(
        ctx => LogOrderCreation(ctx),
        ctx => NotifyWarehouse(ctx),
        ctx => SendConfirmationEmail(ctx))
    // Conditional routing
    .If(ctx => ctx.Instance.RequiresManagerApproval)
        .TransitionTo("AwaitingApproval")
    .Else()
        .TransitionTo("AwaitingPayment")
    .EndIf();
```

## API Reference

### Extension Methods

#### InState()
```csharp
public static DuringStateConfigurator<TSaga> InState<TSaga>(
    this StateMachineBuilder<TSaga> builder,
    string stateName)
```
Define state inline without pre-declaring a State instance.

#### CopyFrom()
```csharp
public static WhenConfigurator<TSaga, TEvent> CopyFrom<TSaga, TEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    Action<TSaga, TEvent> copyAction)
```
Copy data from event to saga using intuitive syntax.

#### SetProperty()
```csharp
public static WhenConfigurator<TSaga, TEvent> SetProperty<TSaga, TEvent, TValue>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    Action<TSaga, TValue> propertySetter,
    Func<StateContext<TSaga, TEvent>, TValue> valueSelector)
```
Set a saga property from a value selector.

#### CompensateWith()
```csharp
public static WhenConfigurator<TSaga, TEvent> CompensateWith<TSaga, TEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    string actionName,
    Func<CancellationToken, Task> compensationAction)
```
Add compensation action with fluent syntax.

#### ThenAll()
```csharp
public static WhenConfigurator<TSaga, TEvent> ThenAll<TSaga, TEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    params Func<StateContext<TSaga, TEvent>, Task>[] actions)
```
Execute multiple actions in sequence.

#### If()
```csharp
public static ConditionalWhenConfigurator<TSaga, TEvent> If<TSaga, TEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    Func<StateContext<TSaga, TEvent>, bool> condition)
```
Add conditional logic to state transition.

#### Else()
```csharp
public ElseConfigurator<TSaga, TEvent> Else()
```
Define alternative branch when condition is false.

#### EndIf()
```csharp
public WhenConfigurator<TSaga, TEvent> EndIf()
```
Complete conditional block and return to main flow.

#### MarkAsCompleted()
```csharp
public static WhenConfigurator<TSaga, TEvent> MarkAsCompleted<TSaga, TEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator)
```
Mark state as final and saga as completed. Alias for `Finalize()`.

#### Publish()
```csharp
public static WhenConfigurator<TSaga, TEvent> Publish<TSaga, TEvent, TPublishEvent>(
    this WhenConfigurator<TSaga, TEvent> configurator,
    Func<StateContext<TSaga, TEvent>, TPublishEvent> eventFactory)
```
Publish an event during state transition.

## Migration Guide

### Step 1: Identify Verbose Patterns
Look for:
- Multiple `Then()` calls that just copy data
- Inline if/else logic in `Then()` blocks
- Manual compensation registration
- Repeated property assignments

### Step 2: Apply Enhanced API
Replace patterns systematically:
- Data copying → `CopyFrom()`
- Property assignment → `SetProperty()`
- If/else logic → `If().Else().EndIf()`
- Compensation → `CompensateWith()`
- Multiple actions → `ThenAll()`

### Step 3: Test Thoroughly
The enhanced API produces the same underlying state machine, but verify:
- All transitions work as expected
- Conditional logic evaluates correctly
- Compensation executes properly
- Integration tests pass

## Best Practices

1. **Use Inline States** - Prefer `InState("StateName")` over pre-declaring states unless you need to reference the same state in many places

2. **Leverage Conditionals** - Use `If().Else()` for branching logic instead of embedding it in `Then()` blocks

3. **Chain Fluently** - Take advantage of method chaining for readability

4. **Group Related Actions** - Use `ThenAll()` to group related operations that should execute together

5. **Name Compensations Clearly** - Use descriptive names for compensation actions to aid debugging

6. **Keep Transitions Simple** - If a transition becomes complex, consider breaking it into multiple states

## Performance

The enhanced API has zero performance overhead - all extensions are compile-time transformations that produce the same underlying state machine structure as the traditional API.

## Compatibility

The enhanced API is 100% compatible with the traditional API. You can mix and match styles within the same state machine:

```csharp
builder.Initially()  // Traditional
    .When(orderCreated)
    .Then(ctx => { /* ... */ })
    .TransitionTo(awaitingPayment);

builder.InState("AwaitingPayment")  // Enhanced
    .When(paymentReceived)
    .SetProperty((saga, id) => saga.PaymentId = id, ctx => ctx.Data.PaymentId)
    .TransitionTo("Completed");
```

## See Also

- [Orchestration Pattern Guide](orchestration-pattern.md)
- [Saga Pattern Overview](saga-pattern.md)
- [State Machine Testing](testing-state-machines.md)
