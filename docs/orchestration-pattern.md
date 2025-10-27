# Orchestration Pattern in HeroMessaging

## Overview

The orchestration pattern in HeroMessaging provides **centralized workflow coordination** through saga state machines. Unlike choreography (where services react to events independently), orchestration uses a central coordinator (the saga orchestrator) that explicitly defines the workflow and manages state transitions.

## Key Concepts

### Saga
A saga is a long-running business process that coordinates multiple operations across services. Each saga instance:
- Has a unique `CorrelationId` linking all related events
- Maintains its current state in the workflow
- Stores business data needed for the workflow
- Tracks version for optimistic concurrency control

### State Machine
Defines the valid states and transitions for a saga:
- **States**: Named stages in the workflow (e.g., "AwaitingPayment", "Completed")
- **Events**: Triggers that cause state transitions
- **Transitions**: Rules defining how events move saga between states
- **Actions**: Code executed during transitions

### Saga Orchestrator
Central coordinator that:
- Routes events to appropriate saga instances
- Executes state transitions
- Persists saga state
- Manages compensation on failures

### Compensation
Mechanism for undoing completed steps when later steps fail:
- Actions are registered during successful operations
- Executed in reverse order (LIFO) when saga fails
- Provides semantic rollback for distributed transactions

## Architecture

```
Event → Orchestrator → State Machine → Action → Saga State
                              ↓
                         Compensation
```

### Components

1. **ISaga**: Base interface for all sagas
2. **SagaBase**: Abstract base class with common functionality
3. **StateMachineBuilder**: Fluent DSL for defining state machines
4. **SagaOrchestrator<TSaga>**: Routes events and executes transitions
5. **ISagaRepository<TSaga>**: Persists saga state with optimistic concurrency
6. **CompensationContext**: Manages compensating actions

## Implementation Guide

### Step 1: Define Your Saga

```csharp
public class OrderSaga : SagaBase
{
    // Business data
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public decimal TotalAmount { get; set; }

    // Tracking data for compensation
    public string? PaymentTransactionId { get; set; }
    public string? InventoryReservationId { get; set; }
    public string? ShipmentTrackingNumber { get; set; }

    // Failure tracking
    public string? FailureReason { get; set; }
}
```

### Step 2: Define Events

Events trigger state transitions in your saga:

```csharp
public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal TotalAmount) : IEvent;

public record PaymentProcessedEvent(
    string OrderId,
    string TransactionId,
    decimal Amount) : IEvent;

public record PaymentFailedEvent(
    string OrderId,
    string Reason) : IEvent;

public record InventoryReservedEvent(
    string OrderId,
    string ReservationId) : IEvent;
```

### Step 3: Define States

```csharp
public static class OrderSagaStates
{
    public static readonly State Initial = new("Initial");
    public static readonly State AwaitingPayment = new("AwaitingPayment");
    public static readonly State AwaitingInventory = new("AwaitingInventory");
    public static readonly State AwaitingShipment = new("AwaitingShipment");
    public static readonly State Completed = new("Completed");
    public static readonly State Failed = new("Failed");
}
```

### Step 4: Build State Machine

Use the fluent DSL to define your workflow:

```csharp
public static StateMachineDefinition<OrderSaga> BuildStateMachine()
{
    var builder = new StateMachineBuilder<OrderSaga>();

    // Define initial transition
    builder.Initially()
        .When(OrderCreated)
            .Then(async ctx =>
            {
                // Extract order data
                ctx.Instance.OrderId = ctx.Data.OrderId;
                ctx.Instance.CustomerId = ctx.Data.CustomerId;
                ctx.Instance.TotalAmount = ctx.Data.TotalAmount;

                // Send ProcessPaymentCommand
                var commandProcessor = ctx.Services.GetRequiredService<ICommandProcessor>();
                await commandProcessor.SendAsync(new ProcessPaymentCommand(
                    ctx.Data.OrderId,
                    ctx.Data.TotalAmount));
            })
            .TransitionTo(AwaitingPayment);

    // Handle successful payment
    builder.During(AwaitingPayment)
        .When(PaymentProcessed)
            .Then(async ctx =>
            {
                ctx.Instance.PaymentTransactionId = ctx.Data.TransactionId;

                // Register compensation: refund payment if later steps fail
                ctx.Compensation.AddCompensation(
                    "RefundPayment",
                    async ct =>
                    {
                        var paymentService = ctx.Services.GetRequiredService<IPaymentService>();
                        await paymentService.RefundAsync(
                            ctx.Instance.PaymentTransactionId!,
                            ctx.Instance.TotalAmount,
                            ct);
                    });

                // Proceed to inventory reservation
                var commandProcessor = ctx.Services.GetRequiredService<ICommandProcessor>();
                await commandProcessor.SendAsync(new ReserveInventoryCommand(
                    ctx.Instance.OrderId!,
                    ctx.Instance.Items));
            })
            .TransitionTo(AwaitingInventory);

    // Handle payment failure
    builder.During(AwaitingPayment)
        .When(PaymentFailed)
            .Then(ctx =>
            {
                ctx.Instance.FailureReason = ctx.Data.Reason;
                return Task.CompletedTask;
            })
            .TransitionTo(Failed);

    // Handle successful inventory reservation
    builder.During(AwaitingInventory)
        .When(InventoryReserved)
            .Then(async ctx =>
            {
                ctx.Instance.InventoryReservationId = ctx.Data.ReservationId;

                // Register compensation: release inventory if shipping fails
                ctx.Compensation.AddCompensation(
                    "ReleaseInventory",
                    async ct =>
                    {
                        var inventoryService = ctx.Services.GetRequiredService<IInventoryService>();
                        await inventoryService.ReleaseAsync(
                            ctx.Instance.InventoryReservationId!,
                            ct);
                    });

                // Proceed to shipping
                var commandProcessor = ctx.Services.GetRequiredService<ICommandProcessor>();
                await commandProcessor.SendAsync(new CreateShipmentCommand(
                    ctx.Instance.OrderId!));
            })
            .TransitionTo(AwaitingShipment);

    // Handle inventory failure - compensate payment
    builder.During(AwaitingInventory)
        .When(InventoryReservationFailed)
            .Then(async ctx =>
            {
                ctx.Instance.FailureReason = ctx.Data.Reason;

                // Compensate all previous steps
                await ctx.Compensation.CompensateAsync();
            })
            .TransitionTo(Failed);

    // Handle successful shipping
    builder.During(AwaitingShipment)
        .When(OrderShipped)
            .Then(ctx =>
            {
                ctx.Instance.ShipmentTrackingNumber = ctx.Data.TrackingNumber;
                return Task.CompletedTask;
            })
            .TransitionTo(Completed)
            .Finalize(); // Mark as final state

    return builder.Build();
}
```

### Step 5: Configure Saga in Startup

```csharp
services.AddHeroMessaging()
    .WithSagaOrchestration(sagas =>
    {
        sagas.AddSaga<OrderSaga>(OrderSagaStateMachine.BuildStateMachine);
    })
    .UseInMemorySagaRepository<OrderSaga>() // For development
    // Or use persistent storage:
    // .UseSagaRepository<OrderSaga, SqlServerSagaRepository<OrderSaga>>()
    .Build();
```

### Step 6: Process Events

The orchestrator automatically routes events to saga instances:

```csharp
// Events are published with correlation ID
var correlationId = Guid.NewGuid();

await eventBus.PublishAsync(new OrderCreatedEvent(
    "ORDER-123",
    "CUST-456",
    99.99m)
{
    CorrelationId = correlationId.ToString()
});

// Orchestrator processes event, creates saga, transitions to AwaitingPayment

await eventBus.PublishAsync(new PaymentProcessedEvent(
    "ORDER-123",
    "TXN-789",
    99.99m)
{
    CorrelationId = correlationId.ToString()
});

// Orchestrator updates saga, transitions to AwaitingInventory
```

## Compensation Patterns

### Simple Compensation

```csharp
ctx.Compensation.AddCompensation(
    "DeleteFile",
    () => File.Delete(filePath));
```

### Async Compensation

```csharp
ctx.Compensation.AddCompensation(
    "RefundPayment",
    async ct =>
    {
        await paymentService.RefundAsync(transactionId, amount, ct);
    });
```

### Complex Compensation

```csharp
ctx.Compensation.AddCompensation(new CustomCompensatingAction(
    "RevertDatabaseChanges",
    async ct =>
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        try
        {
            await db.RollbackChanges(sagaId, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }));
```

### Compensation Execution

Compensation happens automatically when:
1. Saga action explicitly calls `await ctx.Compensation.CompensateAsync()`
2. Saga times out (if timeout handler configured)

```csharp
// Manual compensation on failure
.When(InventoryFailed)
    .Then(async ctx =>
    {
        // Compensate all registered actions (refund payment, etc.)
        await ctx.Compensation.CompensateAsync();
    })
    .TransitionTo(Failed);
```

## Repository Patterns

### In-Memory Repository (Development/Testing)

```csharp
services.AddHeroMessaging()
    .UseInMemorySagaRepository<OrderSaga>()
    .Build();
```

### Custom Repository Implementation

```csharp
public class SqlServerSagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : class, ISaga
{
    private readonly DbContext _db;

    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken ct)
    {
        return await _db.Set<TSaga>()
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
    }

    public async Task UpdateAsync(TSaga saga, CancellationToken ct)
    {
        // Optimistic concurrency control
        var existing = await FindAsync(saga.CorrelationId, ct);
        if (existing == null)
            throw new InvalidOperationException("Saga not found");

        if (existing.Version != saga.Version - 1)
            throw new SagaConcurrencyException(saga.CorrelationId,
                existing.Version, saga.Version);

        _db.Update(saga);
        await _db.SaveChangesAsync(ct);
    }

    // Implement other methods...
}
```

### Repository Registration

```csharp
services.AddHeroMessaging()
    .UseSagaRepository<OrderSaga, SqlServerSagaRepository<OrderSaga>>()
    .Build();
```

## State Machine Patterns

### Parallel Transitions

Multiple events can trigger from the same state:

```csharp
builder.During(Processing)
    .When(SuccessEvent)
        .TransitionTo(Completed)
    .When(FailureEvent)
        .TransitionTo(Failed)
    .When(CancelledEvent)
        .TransitionTo(Cancelled);
```

### Conditional Transitions

Use action logic to determine next state:

```csharp
builder.During(Validating)
    .When(ValidationComplete)
        .Then(ctx =>
        {
            if (ctx.Data.IsValid)
                ctx.Instance.TransitionTo("Approved");
            else
                ctx.Instance.TransitionTo("Rejected");

            return Task.CompletedTask;
        });
```

### Final States

Mark states as final to complete saga:

```csharp
builder.During(Shipping)
    .When(OrderShipped)
        .TransitionTo(Completed)
        .Finalize(); // Marks saga as completed
```

## Best Practices

### 1. Correlation IDs
Always use correlation IDs to link related events:

```csharp
var correlationId = Guid.NewGuid();
var orderCreated = new OrderCreatedEvent(...)
{
    CorrelationId = correlationId.ToString()
};
```

### 2. Idempotency
Saga actions should be idempotent:

```csharp
.Then(async ctx =>
{
    // Check if already processed
    if (ctx.Instance.PaymentTransactionId != null)
        return; // Already processed

    // Process payment
    var transactionId = await ProcessPaymentAsync(...);
    ctx.Instance.PaymentTransactionId = transactionId;
})
```

### 3. Compensation Registration
Register compensations immediately after successful operations:

```csharp
.Then(async ctx =>
{
    // Perform operation
    var resourceId = await AllocateResourceAsync();

    // Immediately register compensation
    ctx.Compensation.AddCompensation(
        "ReleaseResource",
        async ct => await ReleaseResourceAsync(resourceId, ct));
})
```

### 4. Error Handling
Handle both success and failure events:

```csharp
builder.During(AwaitingPayment)
    .When(PaymentProcessed)
        .Then(...)
        .TransitionTo(NextState)
    .When(PaymentFailed)
        .Then(...)
        .TransitionTo(Failed);
```

### 5. State Persistence
Always persist saga state after transitions to handle crashes:

```csharp
// Orchestrator automatically persists after each transition
// Ensure repository is configured correctly:
.UseSagaRepository<OrderSaga, PersistentRepository<OrderSaga>>()
```

### 6. Timeout Handling
Configure timeouts for long-running sagas:

```csharp
services.AddHeroMessaging()
    .WithSagaOrchestration(sagas =>
    {
        sagas.AddSaga<OrderSaga>(...)
            .WithTimeoutHandling(
                checkInterval: TimeSpan.FromMinutes(5),
                defaultTimeout: TimeSpan.FromHours(24));
    })
    .Build();
```

## Monitoring and Observability

### Querying Saga State

```csharp
// Find saga by correlation ID
var saga = await repository.FindAsync(correlationId);

// Find all sagas in a specific state
var pendingSagas = await repository.FindByStateAsync("AwaitingPayment");

// Find stale sagas (for timeout handling)
var staleSagas = await repository.FindStaleAsync(TimeSpan.FromHours(24));
```

### Logging

The orchestrator logs key events:
- Saga creation
- State transitions
- Event processing
- Errors and warnings

```csharp
services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});
```

### TimeProvider Integration

HeroMessaging uses `TimeProvider` for all time-related operations in saga orchestration, enabling deterministic testing and controllable time progression.

**Production Usage (Automatic):**
```csharp
// TimeProvider.System is automatically registered - no configuration needed
builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
builder.UseInMemorySagaRepository<OrderSaga>();
```

**Testing with FakeTimeProvider:**
```csharp
using Microsoft.Extensions.Time.Testing;

// Create fake time provider
var fakeTime = new FakeTimeProvider();
fakeTime.SetUtcNow(DateTimeOffset.Parse("2025-10-27T10:00:00Z"));

// Inject into components
var repository = new InMemorySagaRepository<OrderSaga>(fakeTime);
var orchestrator = new SagaOrchestrator<OrderSaga>(
    repository,
    stateMachine,
    services,
    logger,
    fakeTime);

// Create saga at 10:00
await orchestrator.ProcessAsync(orderCreated);

// Advance time by 2 hours
fakeTime.Advance(TimeSpan.FromHours(2));

// Verify timeout detection
var staleSagas = await repository.FindStaleAsync(TimeSpan.FromHours(1));
```

**Benefits:**
- **Deterministic Tests**: Full control over time progression in tests
- **No Flaky Tests**: Eliminate timing-dependent test failures
- **Framework Support**: Works across netstandard2.0, net6.0, net7.0, net8.0, net9.0
- **Zero Overhead**: .NET 8+ has built-in TimeProvider, older versions use polyfill

**Time-Controlled Operations:**
- Saga CreatedAt/UpdatedAt timestamps
- Stale saga detection (FindStaleAsync)
- Timeout monitoring (SagaTimeoutHandler)
- State transition tracking

## Comparison with Choreography

| Aspect | Orchestration | Choreography |
|--------|---------------|--------------|
| Coordination | Centralized (saga) | Decentralized (events) |
| Visibility | Explicit workflow | Implicit workflow |
| Complexity | Single coordinator | Multiple handlers |
| Compensation | Built-in framework | Manual implementation |
| State Management | Saga stores state | Distributed state |
| Best For | Complex workflows | Simple event chains |

## Examples

See `tests/HeroMessaging.Tests/Examples/OrderSagaExample.cs` for a complete implementation.

## References

- [ADR-0003: State Machine Patterns Research](../docs/adr/0003-state-machine-patterns-research.md)
- [Choreography Pattern](./choreography-pattern.md)
- [ISaga Interface](../src/HeroMessaging.Abstractions/Sagas/ISaga.cs)
- [SagaOrchestrator](../src/HeroMessaging/Orchestration/SagaOrchestrator.cs)
