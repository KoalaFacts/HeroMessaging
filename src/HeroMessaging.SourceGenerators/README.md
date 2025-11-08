# HeroMessaging.SourceGenerators

Roslyn source generators for HeroMessaging to eliminate boilerplate code and improve developer productivity.

## Overview

This package provides compile-time code generation for common HeroMessaging patterns:

- ✅ **Message Validators** - Auto-generate validators from data annotations
- ✅ **Fluent Builders** - Create builder classes for messages
- ✅ **Idempotency Keys** - Generate deterministic keys from message properties
- ✅ **Handler Registration** - Auto-discover and register all handlers in DI

## Installation

```bash
dotnet add package HeroMessaging.SourceGenerators
```

The generators run automatically at compile-time. Generated code appears in your IDE's code completion.

## Generators

### 1. Message Validator Generator

**Trigger**: `[GenerateValidator]` attribute

**What it does**: Generates a validator class based on `System.ComponentModel.DataAnnotations` attributes.

**Example:**

```csharp
using System.ComponentModel.DataAnnotations;
using HeroMessaging.SourceGeneration;

[GenerateValidator]
public record CreateOrderCommand : ICommand
{
    [Required, MaxLength(50)]
    public string OrderId { get; init; } = string.Empty;

    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }

    [EmailAddress]
    public string CustomerEmail { get; init; } = string.Empty;
}
```

**Generated code:**

```csharp
public sealed class CreateOrderCommandValidator
{
    public static ValidationResult Validate(CreateOrderCommand message)
    {
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(message, context, results, validateAllProperties: true))
        {
            return ValidationResult.Success!;
        }

        var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
        return new ValidationResult($"Validation failed: {errors}");
    }

    public static void ValidateAndThrow(CreateOrderCommand message) { /* ... */ }
    public static bool IsValid(CreateOrderCommand message) { /* ... */ }
    public static IReadOnlyList<ValidationResult> GetValidationErrors(CreateOrderCommand message) { /* ... */ }
}
```

**Usage:**

```csharp
var command = new CreateOrderCommand { OrderId = "", Amount = -1 };

// Option 1: Check validity
if (!CreateOrderCommandValidator.IsValid(command))
{
    var errors = CreateOrderCommandValidator.GetValidationErrors(command);
    // Handle errors
}

// Option 2: Validate and throw
CreateOrderCommandValidator.ValidateAndThrow(command); // Throws ValidationException

// Option 3: Get result
var result = CreateOrderCommandValidator.Validate(command);
if (result != ValidationResult.Success)
{
    Console.WriteLine(result.ErrorMessage);
}
```

---

### 2. Message Builder Generator

**Trigger**: `[GenerateBuilder]` attribute

**What it does**: Creates a fluent builder class with `With*` methods for all properties.

**Example:**

```csharp
using HeroMessaging.SourceGeneration;

[GenerateBuilder]
public record OrderCreatedEvent : IEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

**Generated code:**

```csharp
public sealed class OrderCreatedEventBuilder
{
    private string _orderId = string.Empty;
    private string _customerId = string.Empty;
    private decimal _amount;
    private DateTime _createdAt;

    private OrderCreatedEventBuilder() { }

    public static OrderCreatedEventBuilder New() => new();

    public OrderCreatedEventBuilder WithOrderId(string orderId)
    {
        _orderId = orderId;
        return this;
    }

    public OrderCreatedEventBuilder WithCustomerId(string customerId)
    {
        _customerId = customerId;
        return this;
    }

    public OrderCreatedEventBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    public OrderCreatedEventBuilder WithCreatedAt(DateTime createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public OrderCreatedEvent Build()
    {
        return new OrderCreatedEvent
        {
            OrderId = _orderId,
            CustomerId = _customerId,
            Amount = _amount,
            CreatedAt = _createdAt
        };
    }
}
```

**Usage:**

```csharp
// Fluent, readable test data creation
var event = OrderCreatedEventBuilder.New()
    .WithOrderId("ORD-12345")
    .WithCustomerId("CUST-67890")
    .WithAmount(199.99m)
    .WithCreatedAt(DateTime.UtcNow)
    .Build();

// Perfect for test data
[Fact]
public void ProcessOrder_Should_PublishEvent()
{
    var testEvent = OrderCreatedEventBuilder.New()
        .WithOrderId("test-order")
        .WithAmount(100m)
        .Build();

    // Use in test...
}
```

---

### 3. Idempotency Key Generator

**Trigger**: `[GenerateIdempotencyKey(propertyNames)]` attribute

**What it does**: Generates an `IIdempotencyKeyGenerator` implementation that creates deterministic keys from specified properties.

**Example:**

```csharp
using HeroMessaging.SourceGeneration;

[GenerateIdempotencyKey(nameof(OrderId), nameof(CustomerId))]
public record ProcessPaymentCommand : ICommand
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
}
```

**Generated code:**

```csharp
public sealed class ProcessPaymentCommandIdempotencyKeyGenerator : IIdempotencyKeyGenerator
{
    public string GenerateKey(MessageContext context)
    {
        if (context.Message is not ProcessPaymentCommand message)
        {
            throw new InvalidOperationException($"Expected message of type ProcessPaymentCommand, got {context.Message.GetType().Name}");
        }

        var sb = new StringBuilder();
        sb.Append("idempotency:ProcessPaymentCommand");
        sb.Append(':');
        sb.Append(message.OrderId);
        sb.Append(':');
        sb.Append(message.CustomerId);

        return sb.ToString();
    }
}
```

**Usage:**

```csharp
// In your handler setup
services.AddHeroMessaging(builder =>
{
    builder.WithIdempotency(idempotency =>
    {
        idempotency.UseKeyGenerator<ProcessPaymentCommandIdempotencyKeyGenerator>();
    });
});

// The key will be: "idempotency:ProcessPaymentCommand:ORD-123:CUST-456"
// Retrying the same command = same key = cached response
```

**Benefits:**
- **Deterministic**: Same message properties = same key
- **Efficient**: Uses StringBuilder for zero extra allocations
- **Type-safe**: Compile-time checking of property names

---

### 4. Handler Registration Generator

**Trigger**: Automatically runs on all non-abstract classes implementing handler interfaces

**What it does**: Discovers all handlers in your assembly and generates a single registration method.

**Example:**

Your handler classes:
```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task<Result> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // Handle command
    }
}

public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Handle event
    }
}

public class GetOrderHandler : IQueryHandler<GetOrderQuery, Order>
{
    public async Task<Order> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        // Handle query
    }
}
```

**Generated code:**

```csharp
public static class GeneratedHandlerRegistrationExtensions
{
    /// <summary>
    /// Registers all 3 discovered handlers in this assembly.
    /// </summary>
    public static IServiceCollection AddGeneratedHandlers(this IServiceCollection services)
    {
        services.AddTransient(typeof(ICommandHandler<CreateOrderCommand>), typeof(CreateOrderHandler));
        services.AddTransient(typeof(IEventHandler<OrderCreatedEvent>), typeof(OrderCreatedHandler));
        services.AddTransient(typeof(IQueryHandler<GetOrderQuery, Order>), typeof(GetOrderHandler));

        return services;
    }
}
```

**Usage:**

```csharp
// In Startup.cs or Program.cs
services.AddHeroMessaging()
        .AddGeneratedHandlers(); // One line registers all handlers!

// Before (manual):
// services.AddTransient<ICommandHandler<CreateOrderCommand>, CreateOrderHandler>();
// services.AddTransient<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
// services.AddTransient<IQueryHandler<GetOrderQuery, Order>, GetOrderHandler>();
// ... (error-prone, easy to forget)

// After (generated):
// services.AddGeneratedHandlers(); // ✅ Never miss a handler
```

**Benefits:**
- ✅ **No forgotten handlers**: All handlers automatically registered
- ✅ **Refactoring-safe**: Rename/move handlers, registration updates automatically
- ✅ **Performance**: Compile-time discovery (no reflection at runtime)
- ✅ **Visibility**: See exactly what's registered (F12 on AddGeneratedHandlers)

---

### 5. Saga DSL Generator ⭐ NEW

**Trigger**: `[GenerateSaga]` attribute on a partial class

**What it does**: Transforms a declarative DSL into a complete saga state machine with transitions, compensations, and timeouts.

**Example:**

Define your saga using nested classes representing states:

```csharp
using HeroMessaging.SourceGeneration;

// Define saga data
public class OrderSagaData
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentId { get; set; } = string.Empty;
}

// Define events
public record OrderCreatedEvent(string OrderId, decimal Amount);
public record PaymentProcessedEvent(string PaymentId);
public record PaymentFailedEvent(string Reason);

// Define the saga with DSL
[GenerateSaga]
public partial class OrderSaga : SagaBase<OrderSagaData>
{
    // Initial state
    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<OrderCreatedEvent>]
        public async Task OnOrderCreated(OrderCreatedEvent evt)
        {
            Data.OrderId = evt.OrderId;
            Data.Amount = evt.Amount;
            TransitionTo("PaymentPending");
        }
    }

    // Payment processing state
    [SagaState("PaymentPending")]
    public class PaymentPending
    {
        [On<PaymentProcessedEvent>]
        public async Task OnPaymentProcessed(PaymentProcessedEvent evt)
        {
            Data.PaymentId = evt.PaymentId;
            TransitionTo("Completed");
            Complete(); // Mark saga as done
        }

        [On<PaymentFailedEvent>]
        public async Task OnPaymentFailed(PaymentFailedEvent evt)
        {
            Fail($"Payment failed: {evt.Reason}");
        }

        // Timeout after 5 minutes
        [OnTimeout(300)]
        public async Task OnTimeout()
        {
            Fail("Payment timeout");
        }

        // Compensation (rollback)
        [Compensate]
        public async Task CancelPayment()
        {
            // Refund payment logic
        }
    }

    [SagaState("Completed")]
    public class Completed
    {
        // Final state - no transitions
    }
}
```

**Generated code:**

```csharp
public partial class OrderSaga
{
    // State enum for type safety
    public enum States
    {
        Created,
        PaymentPending,
        Completed
    }

    // State machine configuration
    protected override void ConfigureStateMachine()
    {
        Initially("Created");

        During("Created", state =>
        {
            state.When<OrderCreatedEvent>()
                 .Then(async (evt, ct) => await new Created().OnOrderCreated(evt))
                 .Execute();
        });

        During("PaymentPending", state =>
        {
            state.When<PaymentProcessedEvent>()
                 .Then(async (evt, ct) => await new PaymentPending().OnPaymentProcessed(evt))
                 .Execute();

            state.When<PaymentFailedEvent>()
                 .Then(async (evt, ct) => await new PaymentPending().OnPaymentFailed(evt))
                 .Execute();

            state.WithTimeout(TimeSpan.FromSeconds(300),
                async () => await new PaymentPending().OnTimeout());

            state.OnCompensate(async () => await new PaymentPending().CancelPayment());
        });

        During("Completed", state =>
        {
            // No transitions from final state
        });
    }

    // Helper methods
    protected void TransitionTo(string stateName) { State = stateName; }
    protected void Complete() { IsCompleted = true; }
    protected void Fail(string reason) { IsFailed = true; FailureReason = reason; }
}
```

**Usage:**

```csharp
// Create and start the saga
var saga = new OrderSaga { Id = Guid.NewGuid().ToString() };
saga.Data = new OrderSagaData();

// Process events
await saga.ProcessEventAsync(new OrderCreatedEvent("ORD-123", 99.99m));
// Saga transitions to PaymentPending

await saga.ProcessEventAsync(new PaymentProcessedEvent("PAY-456"));
// Saga transitions to Completed

// If payment fails, compensation is called automatically
await saga.ProcessEventAsync(new PaymentFailedEvent("Card declined"));
// Saga marks as failed, compensation runs
```

**DSL Attributes:**

| Attribute | Purpose | Example |
|-----------|---------|---------|
| `[GenerateSaga]` | Mark class for saga generation | `[GenerateSaga] public partial class MySaga` |
| `[SagaState("Name")]` | Define a state | `[SagaState("Processing")] public class Processing` |
| `[InitialState]` | Mark initial state | `[InitialState] [SagaState("Created")]` |
| `[On<EventType>]` | Handle an event | `[On<OrderCreated>] public async Task Handle(...)` |
| `[Compensate]` | Define compensation | `[Compensate] public async Task Rollback()` |
| `[OnTimeout(seconds)]` | Timeout handler | `[OnTimeout(300)] public async Task OnTimeout()` |

**Features:**

✅ **Declarative syntax** - States as nested classes, events as methods
✅ **Type-safe** - Compile-time checking of states and events
✅ **Automatic transitions** - Call `TransitionTo("StateName")`
✅ **Compensations** - Automatic rollback on failure (LIFO order)
✅ **Timeouts** - Automatic timeout handling per state
✅ **Helper methods** - `Complete()`, `Fail()`, `TransitionTo()`
✅ **State enum** - Generated enum for type-safe state references

**Complex Example:**

```csharp
[GenerateSaga]
public partial class CheckoutSaga : SagaBase<CheckoutData>
{
    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<CheckoutStartedEvent>]
        public async Task OnCheckoutStarted(CheckoutStartedEvent evt)
        {
            Data.CartId = evt.CartId;
            Data.Items = evt.Items;
            TransitionTo("ValidatingInventory");
        }
    }

    [SagaState("ValidatingInventory")]
    public class ValidatingInventory
    {
        [On<InventoryValidatedEvent>]
        public async Task OnInventoryValidated(InventoryValidatedEvent evt)
        {
            TransitionTo("ProcessingPayment");
        }

        [On<InventoryInsufficientEvent>]
        public async Task OnInventoryInsufficient(InventoryInsufficientEvent evt)
        {
            Fail("Insufficient inventory");
        }

        [OnTimeout(60)] // 1 minute
        public async Task OnTimeout()
        {
            Fail("Inventory validation timeout");
        }

        [Compensate]
        public async Task ReleaseReservedInventory()
        {
            // Release inventory holds
        }
    }

    [SagaState("ProcessingPayment")]
    public class ProcessingPayment
    {
        [On<PaymentSucceededEvent>]
        public async Task OnPaymentSucceeded(PaymentSucceededEvent evt)
        {
            Data.PaymentId = evt.PaymentId;
            TransitionTo("ShippingOrder");
        }

        [On<PaymentFailedEvent>]
        public async Task OnPaymentFailed(PaymentFailedEvent evt)
        {
            Fail($"Payment failed: {evt.Reason}");
        }

        [OnTimeout(300)] // 5 minutes
        public async Task OnTimeout()
        {
            Fail("Payment processing timeout");
        }

        [Compensate]
        public async Task RefundPayment()
        {
            // Refund the payment
        }
    }

    [SagaState("ShippingOrder")]
    public class ShippingOrder
    {
        [On<ShipmentCreatedEvent>]
        public async Task OnShipmentCreated(ShipmentCreatedEvent evt)
        {
            Data.ShipmentId = evt.ShipmentId;
            TransitionTo("Completed");
            Complete();
        }

        [Compensate]
        public async Task CancelShipment()
        {
            // Cancel the shipment
        }
    }

    [SagaState("Completed")]
    public class Completed { }
}
```

**Benefits:**
- 🎯 **Reduce saga code by 70%** - No manual state machine wiring
- 📖 **Self-documenting** - States and transitions are explicit
- 🔒 **Type-safe** - Compile-time checks for events and states
- 🚀 **Faster development** - Focus on business logic, not plumbing
- ✅ **Testable** - Each state handler is independently testable
- 🔄 **Automatic compensations** - Saga framework handles rollback order

**Compared to Manual Saga:**

Before (manual):
```csharp
public class OrderSaga : SagaBase<OrderSagaData>
{
    protected override void ConfigureStateMachine()
    {
        Initially("Created");
        During("Created", state =>
        {
            state.When<OrderCreatedEvent>()
                 .Then(async (evt, ct) =>
                 {
                     Data.OrderId = evt.OrderId;
                     State = "PaymentPending";
                 })
                 .Execute();
        });
        During("PaymentPending", state =>
        {
            state.When<PaymentProcessedEvent>()
                 .Then(async (evt, ct) =>
                 {
                     Data.PaymentId = evt.PaymentId;
                     State = "Completed";
                     IsCompleted = true;
                 })
                 .Execute();
            state.WithTimeout(TimeSpan.FromSeconds(300), async () =>
            {
                IsFailed = true;
                FailureReason = "Timeout";
            });
            state.OnCompensate(async () =>
            {
                // Refund logic
            });
        });
        // ...more boilerplate
    }
}
```

After (DSL):
```csharp
[GenerateSaga]
public partial class OrderSaga : SagaBase<OrderSagaData>
{
    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<OrderCreatedEvent>]
        public async Task Handle(OrderCreatedEvent evt)
        {
            Data.OrderId = evt.OrderId;
            TransitionTo("PaymentPending");
        }
    }

    [SagaState("PaymentPending")]
    public class PaymentPending
    {
        [On<PaymentProcessedEvent>]
        public async Task Handle(PaymentProcessedEvent evt)
        {
            Data.PaymentId = evt.PaymentId;
            TransitionTo("Completed");
            Complete();
        }

        [OnTimeout(300)]
        public async Task OnTimeout() => Fail("Timeout");

        [Compensate]
        public async Task Compensate() { /* Refund */ }
    }
}
```

**~150 lines → ~30 lines (80% reduction!)**

---

## How It Works

### Compile-Time Code Generation

Source generators run **during compilation**, before your code is built:

```
1. You write code with attributes
2. Compiler calls source generators
3. Generators inspect your syntax trees
4. Generators emit additional C# files
5. Compiler compiles everything together
```

### Viewing Generated Code

**Visual Studio:**
1. Solution Explorer → Dependencies → Analyzers → HeroMessaging.SourceGenerators
2. Expand to see generated files

**VS Code:**
1. Generated files in `obj/Debug/netX.0/generated/HeroMessaging.SourceGenerators/`

**Rider:**
1. Right-click project → Advanced → Show Generated Files

### Debugging Generated Code

You can step into generated code during debugging - it's real C# compiled into your assembly.

---

## Performance

### Zero Runtime Cost

All code generation happens at compile-time:
- ✅ No reflection at runtime
- ✅ No dynamic code generation
- ✅ AOT-friendly (Native AOT, IL trimming compatible)
- ✅ Same performance as hand-written code

### Compilation Impact

Source generators add ~1-2 seconds to build time for typical projects (<100 handlers).

---

## Best Practices

### 1. Use Partial Classes for Extensions

If you need custom logic alongside generated code:

```csharp
[GenerateValidator]
public partial record CreateOrderCommand : ICommand
{
    [Required]
    public string OrderId { get; init; } = string.Empty;

    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }

    // Custom validation method (won't be overwritten)
    partial void OnValidating();
}
```

### 2. Combine Generators

Multiple attributes work together:

```csharp
[GenerateValidator]
[GenerateBuilder]
[GenerateIdempotencyKey(nameof(OrderId))]
public record CreateOrderCommand : ICommand
{
    [Required, MaxLength(50)]
    public string OrderId { get; init; } = string.Empty;

    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }
}

// Usage:
var command = CreateOrderCommandBuilder.New()
    .WithOrderId("ORD-123")
    .WithAmount(99.99m)
    .Build();

CreateOrderCommandValidator.ValidateAndThrow(command);

var keyGen = new CreateOrderCommandIdempotencyKeyGenerator();
var key = keyGen.GenerateKey(context); // "idempotency:CreateOrderCommand:ORD-123"
```

### 3. Use in Test Projects

Builders are especially useful for test data:

```csharp
public class OrderTestData
{
    public static CreateOrderCommand ValidCommand() =>
        CreateOrderCommandBuilder.New()
            .WithOrderId("TEST-001")
            .WithAmount(100m)
            .WithCustomerEmail("test@example.com")
            .Build();

    public static CreateOrderCommand InvalidCommand() =>
        CreateOrderCommandBuilder.New()
            .WithOrderId("") // Invalid
            .WithAmount(-1)  // Invalid
            .Build();
}
```

---

## Troubleshooting

### Generated Code Not Appearing

**Problem**: Attributes applied but no code generated.

**Solutions:**
1. **Clean and rebuild**: `dotnet clean && dotnet build`
2. **Check package reference**: Ensure `HeroMessaging.SourceGenerators` is installed
3. **IDE restart**: Close and reopen Visual Studio/Rider/VS Code
4. **Check build output**: Look for generator warnings/errors

### Compilation Errors in Generated Code

**Problem**: Generated code doesn't compile.

**Common causes:**
1. **Missing property**: `[GenerateIdempotencyKey("MisspelledProperty")]`
   - Fix: Use `nameof(PropertyName)` instead of string literals
2. **Wrong namespace**: Generated code can't find interfaces
   - Fix: Add `using HeroMessaging.Abstractions.Idempotency;`
3. **Partial class mismatch**: Mixing partial/non-partial declarations
   - Fix: Make all declarations `partial`

### Generator Not Running

**Check these:**
1. Target framework supports source generators (.NET 5+)
2. C# language version is recent (C# 9+)
3. Package correctly installed in `.csproj`:
   ```xml
   <PackageReference Include="HeroMessaging.SourceGenerators" Version="1.0.0" />
   ```

---

## Examples Repository

See `examples/SourceGeneratorsDemo/` for complete working examples.

---

## Extending Generators

Want custom generators? Create your own:

```csharp
[Generator]
public class MyCustomGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Your custom logic here
    }
}
```

See [Roslyn Source Generators documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview).

---

## FAQ

**Q: Do generators slow down development?**
A: No. They run incrementally - only regenerate when source changes. Most edits take <100ms.

**Q: Can I see generated code?**
A: Yes! Navigate to generated files in your IDE or check `obj/` directory.

**Q: Are generators production-ready?**
A: Yes. Source generators are a stable feature since C# 9/.NET 5. Used by Microsoft, EF Core, ASP.NET Core.

**Q: Do I need to distribute generated code?**
A: No. Generators run on consumer's machine. Just distribute the generator NuGet package.

**Q: Can I unit test generated code?**
A: Yes, it's normal C# code. Test it like any other code.

---

## Changelog

### Version 1.0.0
- ✅ Message Validator Generator
- ✅ Fluent Builder Generator
- ✅ Idempotency Key Generator
- ✅ Handler Registration Generator

---

## License

MIT License - same as HeroMessaging

---

## Contributing

Found a bug or have an idea for a generator? Open an issue or PR!

**Common generator requests:**
- Saga state machine DSL
- Message serialization optimization
- Metrics/logging boilerplate
- Contract testing generators
