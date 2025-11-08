# Source Generators - Code Examples

Practical, copy-paste ready examples for all HeroMessaging source generators.

## Table of Contents

- [Complete E-Commerce Example](#complete-e-commerce-example)
- [Validator Examples](#validator-examples)
- [Builder Examples](#builder-examples)
- [Idempotency Examples](#idempotency-examples)
- [Handler Registration Examples](#handler-registration-examples)
- [Saga DSL Examples](#saga-dsl-examples)

---

## Complete E-Commerce Example

This example shows all generators working together in a realistic e-commerce scenario.

### 1. Project Setup

```xml
<!-- MyEcommerce.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>

    <!-- Enable generated files visibility -->
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- Source generator reference -->
    <ProjectReference Include="..\HeroMessaging.SourceGenerators\HeroMessaging.SourceGenerators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove="$(CompilerGeneratedFilesOutputPath)\**" />
  </ItemGroup>
</Project>
```

### 2. Messages

```csharp
// Commands/CreateOrderCommand.cs
using System.ComponentModel.DataAnnotations;
using HeroMessaging.SourceGenerators;

namespace MyEcommerce.Commands;

[GenerateValidator]
[GenerateBuilder]
[GenerateIdempotencyKey(nameof(OrderId))]
public record CreateOrderCommand
{
    [Required(ErrorMessage = "Order ID is required")]
    [StringLength(50, MinimumLength = 10)]
    public string OrderId { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string CustomerEmail { get; init; } = string.Empty;

    [Required]
    [MinLength(1, ErrorMessage = "Order must have at least one item")]
    public List<OrderItem> Items { get; init; } = new();

    [Range(0.01, 1000000)]
    public decimal TotalAmount { get; init; }

    public string ShippingAddress { get; init; } = string.Empty;
}

public record OrderItem
{
    [Required]
    public string ProductId { get; init; } = string.Empty;

    [Range(1, 100)]
    public int Quantity { get; init; }

    [Range(0.01, 10000)]
    public decimal UnitPrice { get; init; }
}

// Events/OrderCreatedEvent.cs
[GenerateBuilder]
public record OrderCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

[GenerateBuilder]
public record PaymentProcessedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

[GenerateBuilder]
public record InventoryReservedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public List<string> ReservedItems { get; init; } = new();
}

[GenerateBuilder]
public record ShipmentCreatedEvent
{
    public string OrderId { get; init; } = string.Empty;
    public string TrackingNumber { get; init; } = string.Empty;
}
```

### 3. Handlers

```csharp
// Handlers/CreateOrderHandler.cs
using HeroMessaging.Abstractions;

namespace MyEcommerce.Handlers;

public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;

    public CreateOrderHandler(
        IOrderRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }

    public async Task<ProcessingResult> HandleAsync(
        CreateOrderCommand command,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        // Validate using generated validator
        var validationResult = CreateOrderCommandValidator.Validate(command);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(string.Join(", ", validationResult.Errors));
        }

        // Check idempotency
        var idempotencyKey = command.GetIdempotencyKey();
        if (await _repository.OrderExistsAsync(idempotencyKey))
        {
            return ProcessingResult.Success(); // Already processed
        }

        // Create order
        var order = new Order
        {
            OrderId = command.OrderId,
            CustomerEmail = command.CustomerEmail,
            Items = command.Items,
            TotalAmount = command.TotalAmount,
            Status = OrderStatus.Created
        };

        await _repository.SaveAsync(order, cancellationToken);

        // Publish event using generated builder
        var orderEvent = OrderCreatedEventBuilder.New()
            .WithOrderId(order.OrderId)
            .WithCustomerId(order.CustomerEmail)
            .WithTotalAmount(order.TotalAmount)
            .WithCreatedAt(DateTime.UtcNow)
            .Build();

        await _eventPublisher.PublishAsync(orderEvent);

        return ProcessingResult.Success();
    }
}

// Handlers/OrderCreatedNotificationHandler.cs
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
        await _emailService.SendOrderConfirmationAsync(
            evt.CustomerId,
            evt.OrderId,
            evt.TotalAmount
        );

        return ProcessingResult.Success();
    }
}
```

### 4. Saga

```csharp
// Sagas/OrderFulfillmentSaga.cs
using HeroMessaging.SourceGenerators;
using HeroMessaging.Sagas;

namespace MyEcommerce.Sagas;

public class OrderFulfillmentSagaData
{
    public string OrderId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentTransactionId { get; set; } = string.Empty;
    public List<string> ReservedItems { get; set; } = new();
    public string TrackingNumber { get; set; } = string.Empty;
}

[GenerateSaga]
public partial class OrderFulfillmentSaga : SagaBase<OrderFulfillmentSagaData>
{
    private readonly IPaymentService _paymentService;
    private readonly IInventoryService _inventoryService;
    private readonly IShippingService _shippingService;
    private readonly ILogger<OrderFulfillmentSaga> _logger;

    public OrderFulfillmentSaga(
        IPaymentService paymentService,
        IInventoryService inventoryService,
        IShippingService shippingService,
        ILogger<OrderFulfillmentSaga> logger)
    {
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _shippingService = shippingService;
        _logger = logger;
    }

    [InitialState]
    [SagaState("Created")]
    public class Created
    {
        [On<OrderCreatedEvent>]
        public async Task OnOrderCreated(OrderCreatedEvent evt)
        {
            _logger.LogInformation("Starting order fulfillment for {OrderId}", evt.OrderId);

            Data.OrderId = evt.OrderId;
            Data.TotalAmount = evt.TotalAmount;

            TransitionTo("ProcessingPayment");
        }
    }

    [SagaState("ProcessingPayment")]
    public class ProcessingPayment
    {
        [On<PaymentProcessedEvent>]
        public async Task OnPaymentProcessed(PaymentProcessedEvent evt)
        {
            _logger.LogInformation("Payment processed: {TransactionId}", evt.TransactionId);

            Data.PaymentTransactionId = evt.TransactionId;
            TransitionTo("ReservingInventory");
        }

        [On<PaymentFailedEvent>]
        public async Task OnPaymentFailed(PaymentFailedEvent evt)
        {
            _logger.LogError("Payment failed: {Reason}", evt.Reason);
            Fail($"Payment failed: {evt.Reason}");
        }

        [OnTimeout(300)] // 5 minute timeout
        public async Task OnPaymentTimeout()
        {
            _logger.LogWarning("Payment timeout for order {OrderId}", Data.OrderId);
            Fail("Payment processing timeout");
        }

        [Compensate]
        public async Task RefundPayment()
        {
            if (!string.IsNullOrEmpty(Data.PaymentTransactionId))
            {
                _logger.LogInformation("Refunding payment {TransactionId}", Data.PaymentTransactionId);
                await _paymentService.RefundAsync(Data.PaymentTransactionId);
            }
        }
    }

    [SagaState("ReservingInventory")]
    public class ReservingInventory
    {
        [On<InventoryReservedEvent>]
        public async Task OnInventoryReserved(InventoryReservedEvent evt)
        {
            _logger.LogInformation("Inventory reserved for order {OrderId}", Data.OrderId);

            Data.ReservedItems = evt.ReservedItems;
            TransitionTo("CreatingShipment");
        }

        [On<InventoryUnavailableEvent>]
        public async Task OnInventoryUnavailable(InventoryUnavailableEvent evt)
        {
            _logger.LogError("Inventory unavailable for order {OrderId}", Data.OrderId);
            Fail("Inventory unavailable");
        }

        [Compensate]
        public async Task ReleaseInventory()
        {
            if (Data.ReservedItems.Any())
            {
                _logger.LogInformation("Releasing inventory for order {OrderId}", Data.OrderId);
                await _inventoryService.ReleaseAsync(Data.OrderId, Data.ReservedItems);
            }
        }
    }

    [SagaState("CreatingShipment")]
    public class CreatingShipment
    {
        [On<ShipmentCreatedEvent>]
        public async Task OnShipmentCreated(ShipmentCreatedEvent evt)
        {
            _logger.LogInformation("Shipment created with tracking {TrackingNumber}", evt.TrackingNumber);

            Data.TrackingNumber = evt.TrackingNumber;
            Complete();
        }

        [OnTimeout(600)] // 10 minute timeout
        public async Task OnShipmentTimeout()
        {
            _logger.LogWarning("Shipment creation timeout for order {OrderId}", Data.OrderId);
            Fail("Shipment creation timeout");
        }

        [Compensate]
        public async Task CancelShipment()
        {
            if (!string.IsNullOrEmpty(Data.TrackingNumber))
            {
                _logger.LogInformation("Canceling shipment {TrackingNumber}", Data.TrackingNumber);
                await _shippingService.CancelShipmentAsync(Data.TrackingNumber);
            }
        }
    }
}
```

### 5. Startup Configuration

```csharp
// Program.cs
using HeroMessaging.SourceGenerators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Enable handler auto-discovery
[assembly: GenerateHandlerRegistrations]

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // Register all handlers automatically
    services.AddGeneratedHandlers();

    // Register services
    services.AddSingleton<IOrderRepository, OrderRepository>();
    services.AddSingleton<IPaymentService, PaymentService>();
    services.AddSingleton<IInventoryService, InventoryService>();
    services.AddSingleton<IShippingService, ShippingService>();
    services.AddSingleton<IEmailService, EmailService>();

    // Configure messaging
    services.AddHeroMessaging(messaging =>
    {
        messaging.UseInMemoryMessageBus();
        messaging.AddSaga<OrderFulfillmentSaga>();
    });
});

var host = builder.Build();
await host.RunAsync();
```

### 6. Usage Example

```csharp
// Create order command with validation
var command = CreateOrderCommandBuilder.New()
    .WithOrderId("ORD-2025-001")
    .WithCustomerEmail("customer@example.com")
    .WithItems(new List<OrderItem>
    {
        new() { ProductId = "PROD-123", Quantity = 2, UnitPrice = 49.99m },
        new() { ProductId = "PROD-456", Quantity = 1, UnitPrice = 99.99m }
    })
    .WithTotalAmount(199.97m)
    .WithShippingAddress("123 Main St, City, State 12345")
    .Build();

// Validate before processing
var validationResult = CreateOrderCommandValidator.Validate(command);
if (!validationResult.IsValid)
{
    foreach (var error in validationResult.Errors)
    {
        Console.WriteLine($"Validation error: {error}");
    }
    return;
}

// Get idempotency key for deduplication
var idempotencyKey = command.GetIdempotencyKey();
Console.WriteLine($"Idempotency key: {idempotencyKey}");

// Process command (handler auto-registered)
var messageBus = serviceProvider.GetRequiredService<IMessageBus>();
await messageBus.SendAsync(command);
```

---

## Validator Examples

### Basic Validation

```csharp
[GenerateValidator]
public record RegisterUserCommand
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        ErrorMessage = "Password must contain uppercase, lowercase, and number")]
    public string Password { get; init; } = string.Empty;

    [Range(18, 120)]
    public int Age { get; init; }
}

// Usage
var command = new RegisterUserCommand
{
    Username = "ab", // Too short
    Email = "invalid-email",
    Password = "weak",
    Age = 15
};

var result = RegisterUserCommandValidator.Validate(command);
// result.IsValid = false
// result.Errors contains 4 validation errors
```

### Custom Validation Integration

```csharp
public class ValidationMiddleware : IMessageProcessor
{
    private readonly IMessageProcessor _next;

    public async Task<ProcessingResult> ProcessAsync(
        Message message,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        // Attempt to validate if validator exists
        var messageType = message.GetType();
        var validatorTypeName = $"{messageType.FullName}Validator";
        var validatorType = messageType.Assembly.GetType(validatorTypeName);

        if (validatorType != null)
        {
            var validateMethod = validatorType.GetMethod("Validate",
                BindingFlags.Public | BindingFlags.Static);

            if (validateMethod != null)
            {
                var result = (ValidationResult)validateMethod.Invoke(null, new[] { message })!;

                if (!result.IsValid)
                {
                    throw new ValidationException(
                        $"[VALIDATION_FAILED] {string.Join("; ", result.Errors)}"
                    );
                }
            }
        }

        return await _next.ProcessAsync(message, context, cancellationToken);
    }
}
```

---

## Builder Examples

### Test Data Builders

```csharp
[GenerateBuilder]
public record ProductCreatedEvent
{
    public string ProductId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Category { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

// Test fixtures
public static class TestData
{
    public static ProductCreatedEventBuilder DefaultProduct() =>
        ProductCreatedEventBuilder.New()
            .WithProductId("PROD-TEST")
            .WithName("Test Product")
            .WithPrice(9.99m)
            .WithCategory("Electronics");

    public static ProductCreatedEventBuilder ExpensiveProduct() =>
        DefaultProduct()
            .WithPrice(999.99m)
            .WithCategory("Premium");

    public static ProductCreatedEventBuilder DiscountedProduct() =>
        DefaultProduct()
            .WithPrice(4.99m)
            .WithTags(new List<string> { "sale", "clearance" });
}

// Usage in tests
[Fact]
public async Task ProcessProduct_WithExpensiveProduct_AppliesPremiumShipping()
{
    // Arrange
    var product = TestData.ExpensiveProduct()
        .WithProductId("PROD-001")
        .Build();

    // Act
    var result = await _processor.ProcessAsync(product);

    // Assert
    Assert.Equal(ShippingTier.Premium, result.ShippingTier);
}
```

### Builder Inheritance Pattern

```csharp
public abstract class BaseEventBuilder<T, TBuilder>
    where T : class
    where TBuilder : BaseEventBuilder<T, TBuilder>
{
    protected string _eventId = Guid.NewGuid().ToString();
    protected DateTime _timestamp = DateTime.UtcNow;

    public TBuilder WithEventId(string eventId)
    {
        _eventId = eventId;
        return (TBuilder)this;
    }

    public TBuilder WithTimestamp(DateTime timestamp)
    {
        _timestamp = timestamp;
        return (TBuilder)this;
    }

    public abstract T Build();
}

// Custom builder extending generated one
public class AdvancedOrderBuilder
{
    private readonly CreateOrderCommandBuilder _inner;

    public static AdvancedOrderBuilder New() =>
        new(CreateOrderCommandBuilder.New());

    private AdvancedOrderBuilder(CreateOrderCommandBuilder inner)
    {
        _inner = inner;
    }

    public AdvancedOrderBuilder WithRandomOrderId()
    {
        _inner.WithOrderId($"ORD-{Guid.NewGuid():N}");
        return this;
    }

    public AdvancedOrderBuilder WithTestCustomer()
    {
        _inner.WithCustomerEmail("test@example.com");
        return this;
    }

    public CreateOrderCommand Build() => _inner.Build();
}
```

---

## Idempotency Examples

### Multiple Key Strategies

```csharp
// Simple key - message ID only
[GenerateIdempotencyKey(nameof(MessageId))]
public record SimpleCommand
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
}
// Generated: "idempotency:MSG-123"

// Composite key - multiple properties
[GenerateIdempotencyKey(nameof(AccountId), nameof(TransactionId), nameof(Date))]
public record AccountTransactionCommand
{
    public string AccountId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
}
// Generated: "idempotency:ACC-123:TXN-456:2025-11-08"

// Business key - domain identifiers
[GenerateIdempotencyKey(nameof(CustomerId), nameof(SubscriptionId), nameof(BillingPeriod))]
public record ProcessSubscriptionCommand
{
    public string CustomerId { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    public string BillingPeriod { get; init; } = string.Empty; // "2025-11"
}
// Generated: "idempotency:CUST-789:SUB-012:2025-11"
```

### Idempotency Store Integration

```csharp
public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDatabase _redis;

    public async Task<IdempotencyResponse?> GetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var value = await _redis.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<IdempotencyResponse>(value!);
    }

    public async Task StoreAsync(
        string key,
        IdempotencyResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response);
        await _redis.StringSetAsync(key, json, ttl);
    }
}

// Usage with generated keys
public class IdempotentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    private readonly IIdempotencyStore _store;

    public async Task<ProcessingResult> HandleAsync(
        ProcessPaymentCommand command,
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        // Use generated key method
        var key = command.GetIdempotencyKey();

        // Check cache
        var cached = await _store.GetAsync(key, cancellationToken);
        if (cached != null)
        {
            return cached.Response; // Return cached result
        }

        // Process command
        var result = await ProcessPaymentAsync(command);

        // Cache result
        await _store.StoreAsync(
            key,
            new IdempotencyResponse(result, IdempotencyStatus.Success),
            TimeSpan.FromDays(7),
            cancellationToken
        );

        return result;
    }
}
```

---

## Handler Registration Examples

### Organized Handler Structure

```csharp
// Commands/Handlers/OrderHandlers.cs
namespace MyApp.Commands.Handlers;

public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task<ProcessingResult> HandleAsync(...) { }
}

public class UpdateOrderHandler : ICommandHandler<UpdateOrderCommand>
{
    public async Task<ProcessingResult> HandleAsync(...) { }
}

public class CancelOrderHandler : ICommandHandler<CancelOrderCommand>
{
    public async Task<ProcessingResult> HandleAsync(...) { }
}

// Events/Handlers/NotificationHandlers.cs
namespace MyApp.Events.Handlers;

public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(...) { }
}

public class OrderShippedNotificationHandler : IEventHandler<OrderShippedEvent>
{
    public async Task HandleAsync(...) { }
}

// Mark assembly for generation
[assembly: GenerateHandlerRegistrations]

// All handlers automatically registered!
services.AddGeneratedHandlers();
```

### Multiple Event Handlers

```csharp
// Multiple handlers for same event (all registered)
public class OrderCreatedEmailHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent evt, ...)
    {
        // Send email
    }
}

public class OrderCreatedInventoryHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent evt, ...)
    {
        // Reserve inventory
    }
}

public class OrderCreatedAnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent evt, ...)
    {
        // Track analytics
    }
}

// Generated registration includes all three:
services.AddTransient(typeof(IEventHandler<OrderCreatedEvent>), typeof(OrderCreatedEmailHandler));
services.AddTransient(typeof(IEventHandler<OrderCreatedEvent>), typeof(OrderCreatedInventoryHandler));
services.AddTransient(typeof(IEventHandler<OrderCreatedEvent>), typeof(OrderCreatedAnalyticsHandler));
```

---

## Saga DSL Examples

### Payment Processing Saga

```csharp
public class PaymentSagaData
{
    public string PaymentId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AuthorizationCode { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
}

[GenerateSaga]
public partial class PaymentSaga : SagaBase<PaymentSagaData>
{
    private readonly IPaymentGateway _gateway;

    public PaymentSaga(IPaymentGateway gateway)
    {
        _gateway = gateway;
    }

    [InitialState]
    [SagaState("Authorizing")]
    public class Authorizing
    {
        [On<PaymentInitiatedEvent>]
        public async Task OnInitiated(PaymentInitiatedEvent evt)
        {
            Data.PaymentId = evt.PaymentId;
            Data.Amount = evt.Amount;

            // Authorize payment
            var authResult = await _gateway.AuthorizeAsync(evt.Amount);
            Data.AuthorizationCode = authResult.AuthCode;

            TransitionTo("Authorized");
        }
    }

    [SagaState("Authorized")]
    public class Authorized
    {
        [On<CapturePaymentCommand>]
        public async Task OnCapture(CapturePaymentCommand cmd)
        {
            var captureResult = await _gateway.CaptureAsync(
                Data.AuthorizationCode,
                Data.Amount
            );

            Data.CaptureId = captureResult.CaptureId;
            Complete();
        }

        [On<CancelPaymentCommand>]
        public async Task OnCancel(CancelPaymentCommand cmd)
        {
            Fail("Payment cancelled by user");
        }

        [OnTimeout(900)] // 15 minutes
        public async Task OnTimeout()
        {
            Fail("Authorization expired");
        }

        [Compensate]
        public async Task ReleaseAuthorization()
        {
            await _gateway.VoidAsync(Data.AuthorizationCode);
        }
    }
}
```

### Travel Booking Saga

```csharp
public class TravelBookingSagaData
{
    public string BookingId { get; set; } = string.Empty;
    public string FlightReservation { get; set; } = string.Empty;
    public string HotelReservation { get; set; } = string.Empty;
    public string CarRentalReservation { get; set; } = string.Empty;
}

[GenerateSaga]
public partial class TravelBookingSaga : SagaBase<TravelBookingSagaData>
{
    private readonly IFlightService _flightService;
    private readonly IHotelService _hotelService;
    private readonly ICarRentalService _carRentalService;

    [InitialState]
    [SagaState("BookingFlight")]
    public class BookingFlight
    {
        [On<BookTravelEvent>]
        public async Task OnBookTravel(BookTravelEvent evt)
        {
            Data.BookingId = evt.BookingId;

            var flightReservation = await _flightService.BookAsync(evt.FlightDetails);
            Data.FlightReservation = flightReservation.ConfirmationCode;

            TransitionTo("BookingHotel");
        }

        [Compensate]
        public async Task CancelFlight()
        {
            if (!string.IsNullOrEmpty(Data.FlightReservation))
            {
                await _flightService.CancelAsync(Data.FlightReservation);
            }
        }
    }

    [SagaState("BookingHotel")]
    public class BookingHotel
    {
        [On<FlightBookedEvent>]
        public async Task OnFlightBooked(FlightBookedEvent evt)
        {
            var hotelReservation = await _hotelService.BookAsync(evt.HotelDetails);
            Data.HotelReservation = hotelReservation.ConfirmationCode;

            TransitionTo("BookingCar");
        }

        [On<HotelUnavailableEvent>]
        public async Task OnHotelUnavailable(HotelUnavailableEvent evt)
        {
            Fail("Hotel unavailable - rolling back flight");
        }

        [Compensate]
        public async Task CancelHotel()
        {
            if (!string.IsNullOrEmpty(Data.HotelReservation))
            {
                await _hotelService.CancelAsync(Data.HotelReservation);
            }
        }
    }

    [SagaState("BookingCar")]
    public class BookingCar
    {
        [On<HotelBookedEvent>]
        public async Task OnHotelBooked(HotelBookedEvent evt)
        {
            var carReservation = await _carRentalService.BookAsync(evt.CarDetails);
            Data.CarRentalReservation = carReservation.ConfirmationCode;

            Complete(); // All bookings successful!
        }

        [On<CarUnavailableEvent>]
        public async Task OnCarUnavailable(CarUnavailableEvent evt)
        {
            // Car is optional - complete without it
            Complete();
        }

        [Compensate]
        public async Task CancelCar()
        {
            if (!string.IsNullOrEmpty(Data.CarRentalReservation))
            {
                await _carRentalService.CancelAsync(Data.CarRentalReservation);
            }
        }
    }
}
```

---

For complete documentation, see [USAGE.md](../USAGE.md).
