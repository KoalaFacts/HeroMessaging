# HeroMessaging Source Generators - Usage Guide

Complete guide to using HeroMessaging's Roslyn source generators to reduce boilerplate and improve code quality.

## Table of Contents

- [Installation](#installation)
- [Message Validator Generator](#message-validator-generator)
- [Message Builder Generator](#message-builder-generator)
- [Idempotency Key Generator](#idempotency-key-generator)
- [Handler Registration Generator](#handler-registration-generator)
- [Saga DSL Generator](#saga-dsl-generator)
- [Troubleshooting](#troubleshooting)
- [Best Practices](#best-practices)

## Installation

### 1. Add Source Generator Reference

Add the source generator package to your project:

```xml
<ItemGroup>
  <!-- Reference the source generator -->
  <ProjectReference Include="..\HeroMessaging.SourceGenerators\HeroMessaging.SourceGenerators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 2. Enable Generated Files in IDE

To see generated files in Visual Studio or Rider, add to your `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
  <!-- Don't include generated files in source control -->
  <Compile Remove="$(CompilerGeneratedFilesOutputPath)\**" />
</ItemGroup>
```

### 3. Verify Installation

Build your project and check for generated files:

```bash
dotnet build
ls obj/Debug/net8.0/GeneratedFiles/
```

---

## Message Validator Generator

Automatically generates validation logic from data annotation attributes.

### Step-by-Step Usage

#### 1. Define Your Message with Validation Attributes

```csharp
using System.ComponentModel.DataAnnotations;
using HeroMessaging.SourceGenerators;

namespace MyApp.Messages;

[GenerateValidator]
public record CreateOrderCommand
{
    [Required(ErrorMessage = "Order ID is required")]
    [StringLength(50, MinimumLength = 5)]
    public string OrderId { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string CustomerEmail { get; init; } = string.Empty;

    [Range(0.01, 1000000, ErrorMessage = "Amount must be between 0.01 and 1,000,000")]
    public decimal TotalAmount { get; init; }

    [Range(1, 100)]
    public int ItemCount { get; init; }
}
```

#### 2. Generated Code

The generator creates a validator class:

```csharp
// Generated: CreateOrderCommandValidator.g.cs
public partial class CreateOrderCommandValidator
{
    public static ValidationResult Validate(CreateOrderCommand message)
    {
        var errors = new List<string>();

        // OrderId validation
        if (string.IsNullOrEmpty(message.OrderId))
            errors.Add("Order ID is required");
        else if (message.OrderId.Length < 5 || message.OrderId.Length > 50)
            errors.Add("The field OrderId must be a string with a minimum length of 5 and a maximum length of 50.");

        // CustomerEmail validation
        if (string.IsNullOrEmpty(message.CustomerEmail))
            errors.Add("The CustomerEmail field is required.");
        else if (!IsValidEmail(message.CustomerEmail))
            errors.Add("The CustomerEmail field is not a valid e-mail address.");

        // TotalAmount validation
        if (message.TotalAmount < 0.01m || message.TotalAmount > 1000000m)
            errors.Add("Amount must be between 0.01 and 1,000,000");

        // ItemCount validation
        if (message.ItemCount < 1 || message.ItemCount > 100)
            errors.Add("The field ItemCount must be between 1 and 100.");

        return new ValidationResult(errors.Count == 0, errors);
    }
}
```

#### 3. Use the Validator

```csharp
var command = new CreateOrderCommand
{
    OrderId = "123", // Too short
    CustomerEmail = "invalid-email",
    TotalAmount = -5.00m, // Negative
    ItemCount = 0 // Below minimum
};

var result = CreateOrderCommandValidator.Validate(command);

if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"❌ {error}");
    }
}

// Output:
// ❌ The field OrderId must be a string with a minimum length of 5 and a maximum length of 50.
// ❌ The CustomerEmail field is not a valid e-mail address.
// ❌ Amount must be between 0.01 and 1,000,000
// ❌ The field ItemCount must be between 1 and 100.
```

#### 4. Integration with Message Pipeline

```csharp
public class ValidationMiddleware : IMessageProcessor
{
    private readonly IMessageProcessor _next;

    public ValidationMiddleware(IMessageProcessor next)
    {
        _next = next;
    }

    public async Task<ProcessingResult> ProcessAsync(
        Message message,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        // Use reflection to find validator
        var validatorType = Type.GetType($"{message.GetType().FullName}Validator");
        if (validatorType != null)
        {
            var validateMethod = validatorType.GetMethod("Validate");
            var result = (ValidationResult)validateMethod!.Invoke(null, new[] { message })!;

            if (!result.IsValid)
            {
                throw new ValidationException(
                    $"Message validation failed: {string.Join(", ", result.Errors)}"
                );
            }
        }

        return await _next.ProcessAsync(message, context, cancellationToken);
    }
}
```

### Supported Validation Attributes

- `[Required]` - Field must not be null/empty
- `[StringLength(max, MinimumLength = min)]` - String length constraints
- `[Range(min, max)]` - Numeric range validation
- `[EmailAddress]` - Email format validation
- `[RegularExpression("pattern")]` - Regex pattern matching
- `[MinLength(n)]` - Minimum collection/string length
- `[MaxLength(n)]` - Maximum collection/string length

---

## Message Builder Generator

Generates fluent builder classes for creating messages in tests and application code.

### Step-by-Step Usage

#### 1. Mark Your Message with Attribute

```csharp
using HeroMessaging.SourceGenerators;

namespace MyApp.Messages;

[GenerateBuilder]
public record OrderCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<string> Items { get; init; } = new();
}
```

#### 2. Generated Code

The generator creates a fluent builder:

```csharp
// Generated: OrderCreatedEventBuilder.g.cs
public sealed class OrderCreatedEventBuilder
{
    private string _orderId = string.Empty;
    private string _customerId = string.Empty;
    private decimal _totalAmount = 0m;
    private DateTime _createdAt = DateTime.UtcNow;
    private List<string> _items = new();

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

    public OrderCreatedEventBuilder WithTotalAmount(decimal totalAmount)
    {
        _totalAmount = totalAmount;
        return this;
    }

    public OrderCreatedEventBuilder WithCreatedAt(DateTime createdAt)
    {
        _createdAt = createdAt;
        return this;
    }

    public OrderCreatedEventBuilder WithItems(List<string> items)
    {
        _items = items;
        return this;
    }

    public OrderCreatedEvent Build()
    {
        return new OrderCreatedEvent
        {
            OrderId = _orderId,
            CustomerId = _customerId,
            TotalAmount = _totalAmount,
            CreatedAt = _createdAt,
            Items = _items
        };
    }
}
```

#### 3. Use the Builder

**In Tests:**

```csharp
[Fact]
public async Task OrderProcessor_WithValidOrder_ProcessesSuccessfully()
{
    // Arrange - Build test data fluently
    var orderEvent = OrderCreatedEventBuilder.New()
        .WithOrderId("ORD-12345")
        .WithCustomerId("CUST-999")
        .WithTotalAmount(299.99m)
        .WithCreatedAt(new DateTime(2025, 11, 8))
        .WithItems(new List<string> { "Item1", "Item2" })
        .Build();

    // Act
    var result = await _processor.ProcessAsync(orderEvent);

    // Assert
    Assert.True(result.Success);
}

// Create minimal test data with defaults
var minimalOrder = OrderCreatedEventBuilder.New()
    .WithOrderId("ORD-001")
    .Build();
```

**In Application Code:**

```csharp
public class OrderService
{
    private readonly IEventPublisher _publisher;

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        var orderId = GenerateOrderId();

        // Build and publish event
        var orderEvent = OrderCreatedEventBuilder.New()
            .WithOrderId(orderId)
            .WithCustomerId(request.CustomerId)
            .WithTotalAmount(request.Items.Sum(i => i.Price))
            .WithCreatedAt(DateTime.UtcNow)
            .WithItems(request.Items.Select(i => i.Name).ToList())
            .Build();

        await _publisher.PublishAsync(orderEvent);
    }
}
```

#### 4. Advanced Patterns

**Default Values:**

```csharp
// Builder with smart defaults
var order = OrderCreatedEventBuilder.New()
    .WithOrderId("ORD-123")
    // CreatedAt defaults to DateTime.UtcNow
    // Items defaults to empty list
    .Build();
```

**Chaining for Variations:**

```csharp
// Base test order
var baseBuilder = OrderCreatedEventBuilder.New()
    .WithCustomerId("CUST-999")
    .WithItems(new List<string> { "Widget" });

// Variation 1: Small order
var smallOrder = baseBuilder
    .WithOrderId("ORD-001")
    .WithTotalAmount(10.00m)
    .Build();

// Variation 2: Large order
var largeOrder = baseBuilder
    .WithOrderId("ORD-002")
    .WithTotalAmount(5000.00m)
    .Build();
```

---

## Idempotency Key Generator

Generates deterministic idempotency keys from message properties for exactly-once processing.

### Step-by-Step Usage

#### 1. Mark Message Properties for Key Generation

```csharp
using HeroMessaging.SourceGenerators;

namespace MyApp.Messages;

[GenerateIdempotencyKey(nameof(OrderId), nameof(CustomerId))]
public record ProcessPaymentCommand
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
}
```

#### 2. Generated Code

The generator creates a key generation method:

```csharp
// Generated: ProcessPaymentCommand.IdempotencyKey.g.cs
public partial record ProcessPaymentCommand
{
    public string GetIdempotencyKey()
    {
        return $"idempotency:{OrderId}:{CustomerId}";
    }
}
```

#### 3. Use in Idempotency Store

```csharp
public class IdempotencyMiddleware : IMessageProcessor
{
    private readonly IIdempotencyStore _store;
    private readonly IMessageProcessor _next;

    public async Task<ProcessingResult> ProcessAsync(
        Message message,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        // Generate idempotency key
        var key = message switch
        {
            ProcessPaymentCommand cmd => cmd.GetIdempotencyKey(),
            CreateOrderCommand cmd => cmd.GetIdempotencyKey(),
            _ => $"idempotency:{message.MessageId}" // Fallback
        };

        // Check if already processed
        var cached = await _store.GetAsync(key, cancellationToken);
        if (cached != null)
        {
            _logger.LogInformation("Duplicate message detected: {Key}", key);
            return cached.Response;
        }

        // Process and cache result
        var result = await _next.ProcessAsync(message, context, cancellationToken);

        await _store.StoreAsync(
            key,
            new IdempotencyResponse(result, IdempotencyStatus.Success),
            TimeSpan.FromHours(24),
            cancellationToken
        );

        return result;
    }
}
```

#### 4. Testing Idempotency

```csharp
[Fact]
public async Task ProcessPayment_WithSameKey_ProcessesOnlyOnce()
{
    // Arrange
    var command1 = new ProcessPaymentCommand
    {
        OrderId = "ORD-123",
        CustomerId = "CUST-456",
        Amount = 99.99m,
        PaymentMethod = "CreditCard"
    };

    var command2 = new ProcessPaymentCommand
    {
        OrderId = "ORD-123",
        CustomerId = "CUST-456",
        Amount = 99.99m, // Duplicate request
        PaymentMethod = "CreditCard"
    };

    // Verify same idempotency key
    Assert.Equal(command1.GetIdempotencyKey(), command2.GetIdempotencyKey());
    // Both return: "idempotency:ORD-123:CUST-456"

    // Act
    var result1 = await _processor.ProcessAsync(command1);
    var result2 = await _processor.ProcessAsync(command2); // Should return cached

    // Assert
    _paymentGateway.Verify(x => x.ChargeAsync(It.IsAny<decimal>()), Times.Once);
}
```

#### 5. Advanced Key Patterns

**Multi-Property Keys:**

```csharp
[GenerateIdempotencyKey(nameof(AccountId), nameof(TransactionId), nameof(Date))]
public record RecordTransactionCommand
{
    public string AccountId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
}

// Generated: "idempotency:ACC-123:TXN-456:2025-11-08"
```

**Combining with Content Hash (Manual):**

```csharp
public partial record ProcessPaymentCommand
{
    // Generated method: GetIdempotencyKey()
    // Custom method for content-based deduplication
    public string GetContentHashKey()
    {
        var baseKey = GetIdempotencyKey();
        var contentHash = ComputeHash($"{Amount}:{PaymentMethod}");
        return $"{baseKey}:hash:{contentHash}";
    }
}
```

---

## Handler Registration Generator

Automatically discovers and registers all message handlers in your assembly.

### Step-by-Step Usage

#### 1. Define Your Handlers

```csharp
using HeroMessaging.Abstractions;

namespace MyApp.Handlers;

// Command handler
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProcessingResult> HandleAsync(
        CreateOrderCommand command,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        var order = new Order(command.OrderId, command.CustomerId);
        await _repository.SaveAsync(order, cancellationToken);
        return ProcessingResult.Success();
    }
}

// Query handler
public class GetOrderHandler : IQueryHandler<GetOrderQuery, Order>
{
    private readonly IOrderRepository _repository;

    public GetOrderHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Order> HandleAsync(
        GetOrderQuery query,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(query.OrderId, cancellationToken);
    }
}

// Event handler
public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedNotificationHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task HandleAsync(
        OrderCreatedEvent evt,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        await _emailService.SendOrderConfirmationAsync(evt.OrderId);
        return ProcessingResult.Success();
    }
}
```

#### 2. Mark Assembly for Handler Discovery

Add to your assembly (e.g., in `Program.cs` or separate file):

```csharp
using HeroMessaging.SourceGenerators;

// Add this attribute to enable handler auto-discovery
[assembly: GenerateHandlerRegistrations]
```

#### 3. Generated Code

The generator scans your assembly and creates registration code:

```csharp
// Generated: HandlerRegistrations.g.cs
namespace Microsoft.Extensions.DependencyInjection
{
    public static class GeneratedHandlerRegistrations
    {
        public static IServiceCollection AddGeneratedHandlers(
            this IServiceCollection services)
        {
            // Command handlers
            services.AddTransient(
                typeof(ICommandHandler<CreateOrderCommand>),
                typeof(CreateOrderHandler)
            );

            services.AddTransient(
                typeof(ICommandHandler<ProcessPaymentCommand>),
                typeof(ProcessPaymentHandler)
            );

            // Query handlers
            services.AddTransient(
                typeof(IQueryHandler<GetOrderQuery, Order>),
                typeof(GetOrderHandler)
            );

            services.AddTransient(
                typeof(IQueryHandler<SearchOrdersQuery, List<Order>>),
                typeof(SearchOrdersHandler)
            );

            // Event handlers
            services.AddTransient(
                typeof(IEventHandler<OrderCreatedEvent>),
                typeof(OrderCreatedNotificationHandler)
            );

            services.AddTransient(
                typeof(IEventHandler<OrderCreatedEvent>),
                typeof(OrderCreatedInventoryHandler)
            );

            return services;
        }
    }
}
```

#### 4. Use in Startup Configuration

**ASP.NET Core:**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register all handlers automatically
builder.Services.AddGeneratedHandlers();

// Or chain with other registrations
builder.Services
    .AddHeroMessaging()
    .AddGeneratedHandlers()  // All handlers auto-registered
    .AddLogging();

var app = builder.Build();
```

**Console Application:**

```csharp
// Program.cs
var services = new ServiceCollection();

services
    .AddGeneratedHandlers()  // Automatically registers all handlers
    .AddSingleton<IOrderRepository, InMemoryOrderRepository>()
    .AddSingleton<IEmailService, EmailService>();

var provider = services.BuildServiceProvider();
```

#### 5. Verification

```csharp
[Fact]
public void ServiceProvider_HasAllHandlers_Registered()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddGeneratedHandlers();
    var provider = services.BuildServiceProvider();

    // Act & Assert - Verify handlers are registered
    var createOrderHandler = provider.GetService<ICommandHandler<CreateOrderCommand>>();
    Assert.NotNull(createOrderHandler);
    Assert.IsType<CreateOrderHandler>(createOrderHandler);

    var getOrderHandler = provider.GetService<IQueryHandler<GetOrderQuery, Order>>();
    Assert.NotNull(getOrderHandler);
    Assert.IsType<GetOrderHandler>(getOrderHandler);

    var eventHandler = provider.GetService<IEventHandler<OrderCreatedEvent>>();
    Assert.NotNull(eventHandler);
}
```

#### 6. Benefits

**Before (Manual Registration):**

```csharp
// Must manually register each handler - easy to forget!
services.AddTransient<ICommandHandler<CreateOrderCommand>, CreateOrderHandler>();
services.AddTransient<ICommandHandler<UpdateOrderCommand>, UpdateOrderHandler>();
services.AddTransient<ICommandHandler<CancelOrderCommand>, CancelOrderHandler>();
services.AddTransient<ICommandHandler<ProcessPaymentCommand>, ProcessPaymentHandler>();
// ... 50+ more handlers
```

**After (Auto-Discovery):**

```csharp
// One line registers all handlers
services.AddGeneratedHandlers();
```

---

## Saga DSL Generator

Generates state machine code from declarative saga definitions using nested classes and attributes.

### Step-by-Step Usage

#### 1. Define Saga Data Model

```csharp
namespace MyApp.Sagas;

public class OrderSagaData
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentTransactionId { get; set; } = string.Empty;
    public string ShipmentTrackingId { get; set; } = string.Empty;
}
```

#### 2. Define Saga with DSL

```csharp
using HeroMessaging.SourceGenerators;
using HeroMessaging.Sagas;

namespace MyApp.Sagas;

[GenerateSaga]
public partial class OrderSaga : SagaBase<OrderSagaData>
{
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    private readonly IShippingService _shippingService;

    public OrderSaga(
        IPaymentService paymentService,
        IInventoryService inventoryService,
        IShippingService shippingService)
    {
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _shippingService = shippingService;
    }

    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<OrderCreatedEvent>]
        public async Task OnOrderCreated(OrderCreatedEvent evt)
        {
            Data.OrderId = evt.OrderId;
            Data.CustomerId = evt.CustomerId;
            Data.TotalAmount = evt.TotalAmount;

            TransitionTo("PaymentPending");
        }
    }

    [SagaState("PaymentPending")]
    public class PaymentPending
    {
        [On<PaymentProcessedEvent>]
        public async Task OnPaymentProcessed(PaymentProcessedEvent evt)
        {
            Data.PaymentTransactionId = evt.TransactionId;
            TransitionTo("InventoryReserved");
        }

        [On<PaymentFailedEvent>]
        public async Task OnPaymentFailed(PaymentFailedEvent evt)
        {
            Fail($"Payment failed: {evt.Reason}");
        }

        [OnTimeout(300)] // 5 minutes
        public async Task OnPaymentTimeout()
        {
            Fail("Payment processing timeout");
        }

        [Compensate]
        public async Task RefundPayment()
        {
            if (!string.IsNullOrEmpty(Data.PaymentTransactionId))
            {
                await _paymentService.RefundAsync(Data.PaymentTransactionId);
            }
        }
    }

    [SagaState("InventoryReserved")]
    public class InventoryReserved
    {
        [On<InventoryReservedEvent>]
        public async Task OnInventoryReserved(InventoryReservedEvent evt)
        {
            TransitionTo("ShipmentPending");
        }

        [On<InventoryUnavailableEvent>]
        public async Task OnInventoryUnavailable(InventoryUnavailableEvent evt)
        {
            Fail("Inventory unavailable");
        }

        [Compensate]
        public async Task ReleaseInventory()
        {
            await _inventoryService.ReleaseAsync(Data.OrderId);
        }
    }

    [SagaState("ShipmentPending")]
    public class ShipmentPending
    {
        [On<ShipmentCreatedEvent>]
        public async Task OnShipmentCreated(ShipmentCreatedEvent evt)
        {
            Data.ShipmentTrackingId = evt.TrackingId;
            Complete();
        }

        [OnTimeout(600)] // 10 minutes
        public async Task OnShipmentTimeout()
        {
            Fail("Shipment creation timeout");
        }

        [Compensate]
        public async Task CancelShipment()
        {
            if (!string.IsNullOrEmpty(Data.ShipmentTrackingId))
            {
                await _shippingService.CancelAsync(Data.ShipmentTrackingId);
            }
        }
    }
}
```

#### 3. Generated Code

The generator produces a complete state machine implementation:

```csharp
// Generated: OrderSaga.StateMachine.g.cs
public partial class OrderSaga
{
    public enum States
    {
        Created,
        PaymentPending,
        InventoryReserved,
        ShipmentPending,
        Completed,
        Failed
    }

    private States _currentState = States.Created;

    protected override void ConfigureStateMachine()
    {
        // Created state
        State(() => States.Created)
            .OnEntry(() => _stateInstance = new Created { Saga = this })
            .Permit(OrderCreatedEvent, States.PaymentPending);

        // PaymentPending state
        State(() => States.PaymentPending)
            .OnEntry(() => _stateInstance = new PaymentPending { Saga = this })
            .Permit(PaymentProcessedEvent, States.InventoryReserved)
            .Permit(PaymentFailedEvent, States.Failed)
            .OnTimeout(TimeSpan.FromSeconds(300), OnPaymentTimeout);

        // InventoryReserved state
        State(() => States.InventoryReserved)
            .OnEntry(() => _stateInstance = new InventoryReserved { Saga = this })
            .Permit(InventoryReservedEvent, States.ShipmentPending)
            .Permit(InventoryUnavailableEvent, States.Failed);

        // ShipmentPending state
        State(() => States.ShipmentPending)
            .OnEntry(() => _stateInstance = new ShipmentPending { Saga = this })
            .Permit(ShipmentCreatedEvent, States.Completed)
            .OnTimeout(TimeSpan.FromSeconds(600), OnShipmentTimeout);

        // Compensation configuration
        ConfigureCompensations();
    }

    private void ConfigureCompensations()
    {
        OnCompensate(States.PaymentPending, async () =>
        {
            var state = new PaymentPending { Saga = this };
            await state.RefundPayment();
        });

        OnCompensate(States.InventoryReserved, async () =>
        {
            var state = new InventoryReserved { Saga = this };
            await state.ReleaseInventory();
        });

        OnCompensate(States.ShipmentPending, async () =>
        {
            var state = new ShipmentPending { Saga = this };
            await state.CancelShipment();
        });
    }

    protected void TransitionTo(string stateName)
    {
        _currentState = Enum.Parse<States>(stateName);
        FireStateTransition(_currentState);
    }

    protected void Complete()
    {
        _currentState = States.Completed;
        MarkComplete();
    }

    protected void Fail(string reason)
    {
        _currentState = States.Failed;
        MarkFailed(reason);
        RunCompensations();
    }
}
```

#### 4. Usage in Application

**Saga Initialization:**

```csharp
public class SagaCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISagaRepository _sagaRepository;

    public async Task StartOrderSagaAsync(OrderCreatedEvent evt)
    {
        // Create saga instance
        var saga = _serviceProvider.GetRequiredService<OrderSaga>();

        // Initialize with correlation ID
        saga.Initialize(evt.OrderId);

        // Process initial event
        await saga.ProcessAsync(evt);

        // Persist saga state
        await _sagaRepository.SaveAsync(saga);
    }

    public async Task ContinueSagaAsync(string orderId, object evt)
    {
        // Load saga from storage
        var saga = await _sagaRepository.GetAsync<OrderSaga>(orderId);

        // Process event
        await saga.ProcessAsync(evt);

        // Persist updated state
        await _sagaRepository.SaveAsync(saga);
    }
}
```

**Event Publishing:**

```csharp
// In your services
public class PaymentService : IPaymentService
{
    private readonly IEventPublisher _eventPublisher;

    public async Task ProcessPaymentAsync(string orderId, decimal amount)
    {
        try
        {
            var transactionId = await ChargeCustomerAsync(amount);

            // Publish success event - saga will transition
            await _eventPublisher.PublishAsync(new PaymentProcessedEvent
            {
                OrderId = orderId,
                TransactionId = transactionId,
                Amount = amount
            });
        }
        catch (PaymentException ex)
        {
            // Publish failure event - saga will compensate
            await _eventPublisher.PublishAsync(new PaymentFailedEvent
            {
                OrderId = orderId,
                Reason = ex.Message
            });
        }
    }
}
```

#### 5. Testing Sagas

```csharp
public class OrderSagaTests
{
    [Fact]
    public async Task OrderSaga_HappyPath_CompletesSuccessfully()
    {
        // Arrange
        var paymentService = new Mock<IPaymentService>();
        var inventoryService = new Mock<IInventoryService>();
        var shippingService = new Mock<IShippingService>();

        var saga = new OrderSaga(
            paymentService.Object,
            inventoryService.Object,
            shippingService.Object
        );

        saga.Initialize("ORDER-123");

        // Act - Process events through saga
        await saga.ProcessAsync(new OrderCreatedEvent
        {
            OrderId = "ORDER-123",
            CustomerId = "CUST-456",
            TotalAmount = 299.99m
        });

        await saga.ProcessAsync(new PaymentProcessedEvent
        {
            TransactionId = "TXN-789"
        });

        await saga.ProcessAsync(new InventoryReservedEvent());

        await saga.ProcessAsync(new ShipmentCreatedEvent
        {
            TrackingId = "SHIP-999"
        });

        // Assert
        Assert.Equal(OrderSaga.States.Completed, saga.CurrentState);
        Assert.Equal("ORDER-123", saga.Data.OrderId);
        Assert.Equal("TXN-789", saga.Data.PaymentTransactionId);
        Assert.Equal("SHIP-999", saga.Data.ShipmentTrackingId);
    }

    [Fact]
    public async Task OrderSaga_PaymentFails_RunsCompensation()
    {
        // Arrange
        var paymentService = new Mock<IPaymentService>();
        var saga = new OrderSaga(
            paymentService.Object,
            Mock.Of<IInventoryService>(),
            Mock.Of<IShippingService>()
        );

        saga.Initialize("ORDER-123");

        // Act - Payment fails
        await saga.ProcessAsync(new OrderCreatedEvent
        {
            OrderId = "ORDER-123",
            TotalAmount = 299.99m
        });

        await saga.ProcessAsync(new PaymentFailedEvent
        {
            Reason = "Insufficient funds"
        });

        // Assert
        Assert.Equal(OrderSaga.States.Failed, saga.CurrentState);

        // Verify compensation was executed
        paymentService.Verify(
            x => x.RefundAsync(It.IsAny<string>()),
            Times.Once
        );
    }

    [Fact]
    public async Task OrderSaga_PaymentTimeout_Compensates()
    {
        // Arrange
        var saga = new OrderSaga(
            Mock.Of<IPaymentService>(),
            Mock.Of<IInventoryService>(),
            Mock.Of<IShippingService>()
        );

        saga.Initialize("ORDER-123");

        // Act
        await saga.ProcessAsync(new OrderCreatedEvent
        {
            OrderId = "ORDER-123"
        });

        // Simulate timeout
        await Task.Delay(TimeSpan.FromSeconds(301));
        await saga.CheckTimeoutsAsync();

        // Assert
        Assert.Equal(OrderSaga.States.Failed, saga.CurrentState);
    }
}
```

#### 6. DSL Attributes Reference

| Attribute | Usage | Description |
|-----------|-------|-------------|
| `[GenerateSaga]` | Class | Marks a class for saga generation |
| `[SagaState("Name")]` | Nested class | Defines a state in the saga |
| `[InitialState]` | Nested class | Marks the initial state |
| `[On<EventType>]` | Method | Event handler for this state |
| `[OnTimeout(seconds)]` | Method | Timeout handler for this state |
| `[Compensate]` | Method | Compensation logic for this state |

---

## Troubleshooting

### Generated Files Not Appearing

**Problem:** Can't see generated files in IDE

**Solution:**

1. Enable compiler-generated files in `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

2. Rebuild the project:

```bash
dotnet clean
dotnet build
```

3. Check output directory:

```bash
ls obj/Debug/net8.0/GeneratedFiles/HeroMessaging.SourceGenerators/
```

### Generator Not Running

**Problem:** Attribute exists but no code is generated

**Solution:**

1. Verify generator is referenced correctly:

```xml
<ProjectReference Include="..\HeroMessaging.SourceGenerators\HeroMessaging.SourceGenerators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

2. Check build output for generator diagnostics:

```bash
dotnet build --verbosity detailed | grep "SourceGenerator"
```

3. Verify attribute namespace is imported:

```csharp
using HeroMessaging.SourceGenerators; // Required!
```

### Compilation Errors in Generated Code

**Problem:** Generated code doesn't compile

**Solution:**

1. Check that required base classes exist:
   - `SagaBase<TData>` for saga generator
   - Validation attributes for validator generator

2. Verify property types are supported:
   - Primitives (string, int, decimal, etc.)
   - DateTime, DateTimeOffset
   - Collections (List<T>, etc.)

3. Check namespace conflicts - generated code uses your namespace

### Performance Issues

**Problem:** Build is slow with generators

**Solution:**

1. Generators use incremental generation - only affected files are regenerated

2. Check if you're triggering unnecessary rebuilds:

```bash
# Use incremental build
dotnet build

# Avoid full rebuild unless necessary
# dotnet clean && dotnet build
```

3. Monitor generator performance:

```bash
dotnet build -p:ReportAnalyzer=true
```

### Debugging Generators

**Problem:** Need to debug generator logic

**Solution:**

1. Add diagnostic output in generator:

```csharp
context.ReportDiagnostic(Diagnostic.Create(
    new DiagnosticDescriptor(
        "SG0001",
        "Generator Debug",
        "Processing class: {0}",
        "SourceGenerator",
        DiagnosticSeverity.Info,
        true
    ),
    Location.None,
    className
));
```

2. Attach debugger to generator process (advanced):

```csharp
// Add to generator Initialize method
if (!Debugger.IsAttached)
{
    Debugger.Launch();
}
```

---

## Best Practices

### 1. Use Partial Classes/Records

Generators add to existing types using `partial`:

```csharp
// Good - partial allows generator to extend
public partial record OrderCommand { }

// Bad - generator can't add to sealed class
public sealed record OrderCommand { }
```

### 2. Organize Generated Files

Add to `.gitignore`:

```
# Generated files
obj/
bin/
**/GeneratedFiles/
```

Keep source control clean - generated files are reproducible.

### 3. Document Generated Attributes

```csharp
/// <summary>
/// Order creation command. Validator auto-generated from attributes.
/// Builder auto-generated with OrderBuilder.New() pattern.
/// </summary>
[GenerateValidator]
[GenerateBuilder]
public record CreateOrderCommand { ... }
```

### 4. Combine Generators

```csharp
// Stack multiple generators on one type
[GenerateValidator]
[GenerateBuilder]
[GenerateIdempotencyKey(nameof(OrderId))]
public record CreateOrderCommand
{
    [Required]
    public string OrderId { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

// Use together:
var command = CreateOrderCommandBuilder.New()
    .WithOrderId("ORD-123")
    .WithAmount(99.99m)
    .Build();

var validationResult = CreateOrderCommandValidator.Validate(command);
var idempotencyKey = command.GetIdempotencyKey();
```

### 5. Test Generated Code

```csharp
[Fact]
public void GeneratedValidator_WithInvalidData_ReturnsErrors()
{
    // Even though validation is generated, test it!
    var invalid = new CreateOrderCommand { OrderId = "" };
    var result = CreateOrderCommandValidator.Validate(invalid);

    Assert.False(result.IsValid);
    Assert.Contains("Order ID is required", result.Errors);
}
```

### 6. Use Builders in Tests

```csharp
// Create base test fixture builders
public class TestFixtures
{
    public static OrderCreatedEventBuilder ValidOrder() =>
        OrderCreatedEventBuilder.New()
            .WithOrderId("ORD-TEST")
            .WithCustomerId("CUST-TEST")
            .WithTotalAmount(100.00m);
}

// Use in tests
[Fact]
public async Task Test_WithValidOrder()
{
    var order = TestFixtures.ValidOrder()
        .WithOrderId("ORD-SPECIFIC")
        .Build();

    // ...
}
```

### 7. Saga State Diagram

Document your saga states visually:

```
[Created] --OrderCreated--> [PaymentPending]
    |                            |
    |                            +--PaymentProcessed--> [InventoryReserved]
    |                            |
    |                            +--PaymentFailed-----> [Failed]
    |                            |
    |                            +--Timeout(5min)-----> [Failed]
    |
[InventoryReserved] --InventoryReserved--> [ShipmentPending]
    |
    +--InventoryUnavailable--> [Failed]

[ShipmentPending] --ShipmentCreated--> [Completed]
    |
    +--Timeout(10min)--------> [Failed]

[Failed] --triggers--> Compensations (reverse order)
```

---

## Summary

The HeroMessaging Source Generators provide:

1. **Message Validator Generator** - Validation from data annotations
2. **Message Builder Generator** - Fluent test data builders
3. **Idempotency Key Generator** - Deterministic deduplication keys
4. **Handler Registration Generator** - Auto-discovery of all handlers
5. **Saga DSL Generator** - Declarative state machine definitions

### Quick Reference

```csharp
// Validation
[GenerateValidator]
public record MyCommand { [Required] string Id { get; init; } }
var result = MyCommandValidator.Validate(cmd);

// Building
[GenerateBuilder]
public record MyEvent { string Id { get; init; } }
var evt = MyEventBuilder.New().WithId("123").Build();

// Idempotency
[GenerateIdempotencyKey(nameof(Id))]
public record MyCommand { string Id { get; init; } }
var key = cmd.GetIdempotencyKey();

// Registration
[assembly: GenerateHandlerRegistrations]
services.AddGeneratedHandlers();

// Sagas
[GenerateSaga]
public partial class MySaga : SagaBase<MyData>
{
    [InitialState]
    [SagaState("Start")]
    public class Start
    {
        [On<MyEvent>]
        public async Task Handle(MyEvent evt) { ... }
    }
}
```

For more details, see [README.md](README.md) and individual generator files in `Generators/`.
