# HeroMessaging Source Generators - Usage Guide

Complete guide to using HeroMessaging's Roslyn source generators to reduce boilerplate and improve code quality.

## Table of Contents

- [Installation](#installation)
- [Message Validator Generator](#message-validator-generator)
- [Message Builder Generator](#message-builder-generator)
- [Sophisticated Test Data Builder Generator](#sophisticated-test-data-builder-generator)
- [Idempotency Key Generator](#idempotency-key-generator)
- [Handler Registration Generator](#handler-registration-generator)
- [Saga DSL Generator](#saga-dsl-generator)
- [Method Logging Generator](#method-logging-generator)
- [Metrics Instrumentation Generator](#metrics-instrumentation-generator)
- [Contract Testing Generator](#contract-testing-generator)
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

## Sophisticated Test Data Builder Generator

Generates advanced test data builders with auto-randomization, object mothers, and collection support. More powerful than basic [GenerateBuilder] with realistic fake data generation.

### Step-by-Step Usage

#### 1. Mark Your Model with Attribute

```csharp
using HeroMessaging.SourceGenerators;

namespace MyApp.Models;

[GenerateTestDataBuilder]
public record Order
{
    [RandomString(Prefix = "ORD-", Length = 8)]
    public string OrderId { get; init; } = string.Empty;

    [RandomEmail(Domain = "example.com")]
    public string CustomerEmail { get; init; } = string.Empty;

    [RandomDecimal(Min = 1.00, Max = 10000.00, DecimalPlaces = 2)]
    public decimal Amount { get; init; }

    [RandomInt(Min = 1, Max = 100)]
    public int Quantity { get; init; }

    [RandomDateTime(DaysFromNow = -90, DaysToNow = 0)]
    public DateTime OrderDate { get; init; }

    [RandomEnum]
    public OrderStatus Status { get; init; }

    [RandomCollection(MinCount = 1, MaxCount = 5)]
    public List<OrderItem> Items { get; init; } = new();
}

public record OrderItem
{
    [RandomString(Prefix = "PROD-", Length = 6)]
    public string ProductId { get; init; } = string.Empty;

    [RandomInt(Min = 1, Max = 10)]
    public int Quantity { get; init; }

    [RandomDecimal(Min = 1.00, Max = 500.00)]
    public decimal UnitPrice { get; init; }
}
```

#### 2. Generated Test Data Builder

```csharp
// Generated: TestData.Order.g.cs
public static partial class TestData
{
    public static OrderBuilder Order() => new OrderBuilder();

    public class OrderBuilder
    {
        private string _orderId;
        private string _customerEmail;
        private decimal _amount;
        private int _quantity;
        private DateTime _orderDate;
        private OrderStatus _status;
        private List<OrderItem> _items;

        // With methods for all properties
        public OrderBuilder WithOrderId(string value) { ... }
        public OrderBuilder WithCustomerEmail(string value) { ... }
        // ... etc

        // Auto-randomization based on attributes
        public OrderBuilder WithRandomData()
        {
            _orderId = "ORD-" + GenerateRandomString(8);  // Uses [RandomString]
            _customerEmail = "user" + Random.Next(1000, 9999) + "@example.com";  // [RandomEmail]
            _amount = Math.Round((decimal)(Random.NextDouble() * 9999), 2);  // [RandomDecimal]
            _quantity = Random.Next(1, 101);  // [RandomInt]
            _orderDate = DateTime.UtcNow.AddDays(Random.Next(-90, 1));  // [RandomDateTime]
            _status = (OrderStatus)Random.Next(0, EnumValues.Length);  // [RandomEnum]

            // Auto-populate collection with random items
            _items = new List<OrderItem>();
            var count = Random.Next(1, 6);  // [RandomCollection]
            for (int i = 0; i < count; i++)
            {
                _items.Add(TestData.OrderItem().WithRandomData().Build());
            }

            return this;
        }

        public Order Build() { ... }

        // Collection builders
        public List<Order> CreateMany(int count)
        {
            var items = new List<Order>();
            for (int i = 0; i < count; i++)
            {
                items.Add(new OrderBuilder().WithRandomData().Build());
            }
            return items;
        }
    }
}
```

#### 3. Basic Usage - Auto-Randomization

```csharp
// Create order with all random data
var order = TestData.Order()
    .WithRandomData()
    .Build();

// Results in:
// OrderId: "ORD-AB12CD34"
// CustomerEmail: "user5678@example.com"
// Amount: 4537.82m
// Quantity: 47
// OrderDate: DateTime.UtcNow.AddDays(-23)
// Status: OrderStatus.Created
// Items: 3 random items
```

#### 4. Override Specific Fields

```csharp
// Randomize most fields, but override specific ones
var order = TestData.Order()
    .WithRandomData()  // Fill with random data
    .WithOrderId("ORD-SPECIFIC")  // Override just what you need
    .WithAmount(999.99m)
    .Build();

// Results in:
// OrderId: "ORD-SPECIFIC" (overridden)
// CustomerEmail: "user1234@example.com" (random)
// Amount: 999.99m (overridden)
// Quantity: 23 (random)
// ...
```

#### 5. Create Collections

```csharp
// Create 10 random orders
var orders = TestData.Order().CreateMany(10);

// All orders have unique random data
foreach (var order in orders)
{
    Console.WriteLine($"{order.OrderId}: {order.Amount:C}");
}

// Output:
// ORD-A1B2C3D4: $2,345.67
// ORD-E5F6G7H8: $876.54
// ORD-I9J0K1L2: $5,432.10
// ...
```

#### 6. Object Mother Pattern (Predefined Scenarios)

Define common test scenarios in your model:

```csharp
[GenerateTestDataBuilder]
public record Order
{
    // Properties...

    [BuilderScenario("Valid")]
    public static Order ValidScenario() => new()
    {
        OrderId = "ORD-VALID",
        CustomerEmail = "[email protected]",
        Amount = 99.99m,
        Quantity = 1,
        Status = OrderStatus.Created,
        Items = new() { /* ... */ }
    };

    [BuilderScenario("Expensive")]
    public static Order ExpensiveScenario() => new()
    {
        OrderId = "ORD-VIP",
        Amount = 9999.99m,
        Quantity = 10,
        Status = OrderStatus.Created
    };

    [BuilderScenario("Invalid")]
    public static Order InvalidScenario() => new()
    {
        OrderId = "",  // Invalid
        Amount = -50m,  // Invalid
        Quantity = 0  // Invalid
    };
}

// Generated builder methods:
var validOrder = TestData.ValidOrder().Build();
var expensiveOrder = TestData.ExpensiveOrder().Build();
var invalidOrder = TestData.InvalidOrder().Build();

// Still customizable:
var customValid = TestData.ValidOrder()
    .WithAmount(149.99m)
    .Build();
```

#### 7. Random Attribute Options

**RandomString:**
```csharp
[RandomString(Prefix = "ORD-", Suffix = "-2025", Length = 8, CharSet = RandomStringCharSet.Alphanumeric)]
public string OrderId { get; init; }
// Generates: "ORD-A1B2C3D4-2025"

[RandomString(Length = 6, CharSet = RandomStringCharSet.Numeric)]
public string InvoiceNumber { get; init; }
// Generates: "123456"
```

**RandomEmail:**
```csharp
[RandomEmail(Domain = "mycompany.com")]
public string Email { get; init; }
// Generates: "user5678@mycompany.com"
```

**RandomInt:**
```csharp
[RandomInt(Min = 1, Max = 100)]
public int Quantity { get; init; }
// Generates: 1-100

[RandomInt(Min = 18, Max = 65)]
public int Age { get; init; }
// Generates: 18-65
```

**RandomDecimal:**
```csharp
[RandomDecimal(Min = 0.01, Max = 10000.00, DecimalPlaces = 2)]
public decimal Price { get; init; }
// Generates: 0.01 - 10000.00 with 2 decimal places

[RandomDecimal(Min = 0.001, Max = 1.000, DecimalPlaces = 3)]
public decimal Percentage { get; init; }
// Generates: 0.001 - 1.000 with 3 decimal places
```

**RandomDateTime:**
```csharp
[RandomDateTime(DaysFromNow = -365, DaysToNow = 0)]
public DateTime CreatedDate { get; init; }
// Generates: Random date in past year

[RandomDateTime(DaysFromNow = 0, DaysToNow = 30)]
public DateTime DueDate { get; init; }
// Generates: Random date in next 30 days

[RandomDateTime(DaysFromNow = -7, DaysToNow = 7, UseUtc = true)]
public DateTime ModifiedDate { get; init; }
// Generates: Random date ± 7 days from now (UTC)
```

**RandomGuid:**
```csharp
[RandomGuid(Format = "N")]  // 32 digits
public string TransactionId { get; init; }
// Generates: "00000000000000000000000000000000"

[RandomGuid(Format = "D")]  // Hyphens (default)
public string CorrelationId { get; init; }
// Generates: "00000000-0000-0000-0000-000000000000"
```

**RandomEnum:**
```csharp
[RandomEnum]
public OrderStatus Status { get; init; }
// Generates: Random value from OrderStatus enum

[RandomEnum(Exclude = "Cancelled,Deleted")]
public OrderStatus ActiveStatus { get; init; }
// Generates: Random value excluding Cancelled and Deleted
```

**RandomCollection:**
```csharp
[RandomCollection(MinCount = 1, MaxCount = 10)]
public List<OrderItem> Items { get; init; }
// Generates: 1-10 random OrderItems automatically

[RandomCollection(MinCount = 0, MaxCount = 3)]
public List<string> Tags { get; init; }
// Generates: 0-3 random tags
```

#### 8. In Unit Tests

```csharp
[Fact]
public void ProcessOrder_WithValidOrder_Succeeds()
{
    // Arrange - Quick random test data
    var order = TestData.Order()
        .WithRandomData()
        .WithStatus(OrderStatus.Created)
        .Build();

    var processor = new OrderProcessor();

    // Act
    var result = processor.Process(order);

    // Assert
    Assert.True(result.Success);
    Assert.NotEmpty(order.OrderId);
    Assert.True(order.Amount > 0);
}

[Theory]
[InlineData(10)]
[InlineData(50)]
[InlineData(100)]
public void ProcessBatch_WithMultipleOrders_ProcessesAll(int count)
{
    // Arrange - Create multiple random orders easily
    var orders = TestData.Order().CreateMany(count);

    var processor = new BatchProcessor();

    // Act
    var result = processor.ProcessBatch(orders);

    // Assert
    Assert.Equal(count, result.ProcessedCount);
}

[Fact]
public void ValidateOrder_WithInvalidData_ReturnErrors()
{
    // Arrange - Use predefined invalid scenario
    var invalidOrder = TestData.InvalidOrder().Build();

    var validator = new OrderValidator();

    // Act
    var result = validator.Validate(invalidOrder);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains("OrderId is required", result.Errors);
    Assert.Contains("Amount must be positive", result.Errors);
}
```

#### 9. Test Fixtures and Reusable Builders

```csharp
public class OrderTestFixtures
{
    // Create reusable test data patterns
    public static Order SmallOrder() =>
        TestData.Order()
            .WithRandomData()
            .WithAmount(50.00m)
            .WithQuantity(1)
            .Build();

    public static Order LargeOrder() =>
        TestData.Order()
            .WithRandomData()
            .WithAmount(5000.00m)
            .WithQuantity(100)
            .Build();

    public static Order PaidOrder() =>
        TestData.Order()
            .WithRandomData()
            .WithStatus(OrderStatus.Paid)
            .Build();

    public static List<Order> MixedOrders() =>
        new List<Order>
        {
            SmallOrder(),
            LargeOrder(),
            TestData.Order().WithRandomData().WithStatus(OrderStatus.Cancelled).Build()
        };
}

// Use in tests:
var order = OrderTestFixtures.SmallOrder();
var orders = OrderTestFixtures.MixedOrders();
```

### Benefits Over Basic Builder

**Basic Builder ([GenerateBuilder]):**
- Manual value assignment
- No randomization
- Requires setting every property
- No collection support
- No test scenarios

**Sophisticated Test Data Builder ([GenerateTestDataBuilder]):**
- ✅ Auto-randomization with realistic data
- ✅ Attribute-based constraints
- ✅ Collection creation (CreateMany)
- ✅ Object Mother patterns (predefined scenarios)
- ✅ Quick test data generation
- ✅ Type-safe random generation
- ✅ Minimal test code

### Comparison

**Before (Manual Test Data):**

```csharp
// Every test needs this boilerplate
var order = new Order
{
    OrderId = "ORD-" + Guid.NewGuid().ToString().Substring(0, 8),
    CustomerEmail = "test" + Random.Next(1000, 9999) + "@example.com",
    Amount = (decimal)(Random.NextDouble() * 1000),
    Quantity = Random.Next(1, 100),
    OrderDate = DateTime.UtcNow.AddDays(-Random.Next(0, 90)),
    Status = OrderStatus.Created,
    Items = new List<OrderItem>
    {
        new() { ProductId = "PROD-1", Quantity = 1, UnitPrice = 99.99m },
        new() { ProductId = "PROD-2", Quantity = 2, UnitPrice = 49.99m }
    }
};

// Create 10 orders - need loop
var orders = new List<Order>();
for (int i = 0; i < 10; i++)
{
    orders.Add(new Order { /* repeat all above */ });
}
```

**After (Generated Builder):**

```csharp
// One line
var order = TestData.Order().WithRandomData().Build();

// Collections - one line
var orders = TestData.Order().CreateMany(10);

// Scenarios - one line
var validOrder = TestData.ValidOrder().Build();
```

**Savings: 95% less test code!**

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

## Method Logging Generator

Automatically generates logging code for methods, eliminating repetitive entry/exit/duration/error logging.

### Step-by-Step Usage

#### 1. Define Partial Method with Attribute

```csharp
using HeroMessaging.SourceGenerators;
using Microsoft.Extensions.Logging;

namespace MyApp.Services;

public partial class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderRepository _repository;

    public OrderService(ILogger<OrderService> logger, IOrderRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    // Define partial method with [LogMethod]
    [LogMethod(LogLevel.Information)]
    public partial Task<Order> CreateOrderAsync(string orderId, decimal amount);

    // Implementation goes in Core method
    private async partial Task<Order> CreateOrderCore(string orderId, decimal amount)
    {
        var order = new Order { OrderId = orderId, Amount = amount };
        await _repository.SaveAsync(order);
        return order;
    }
}
```

#### 2. Generated Code

The generator creates the logging wrapper:

```csharp
// Generated: OrderService.CreateOrderAsync.Logging.g.cs
public partial class OrderService
{
    public async partial Task<Order> CreateOrderAsync(string orderId, decimal amount)
    {
        using var activity = Activity.Current?.Source.StartActivity("OrderService.CreateOrderAsync");
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Entering CreateOrderAsync with orderId={OrderId}, amount={Amount}",
            orderId, amount);

        try
        {
            var result = await CreateOrderCore(orderId, amount);

            stopwatch.Stop();
            _logger.LogInformation("Completed CreateOrderAsync in {DurationMs}ms",
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed CreateOrderAsync after {DurationMs}ms",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
```

#### 3. Protecting Sensitive Data

Use `[NoLog]` to exclude sensitive parameters from logs:

```csharp
[LogMethod(LogLevel.Information)]
public partial Task<PaymentResult> ProcessPaymentAsync(
    string orderId,
    decimal amount,
    [NoLog] string creditCardNumber,  // Won't be logged
    string paymentMethod);

private async partial Task<PaymentResult> ProcessPaymentCore(
    string orderId,
    decimal amount,
    string creditCardNumber,
    string paymentMethod)
{
    // Payment processing logic
    var result = await _gateway.ChargeAsync(creditCardNumber, amount);
    return result;
}

// Generated log message:
// "Entering ProcessPaymentAsync with orderId={OrderId}, amount={Amount}, paymentMethod={PaymentMethod}"
// Notice creditCardNumber is excluded
```

#### 4. Custom Log Messages

Override default messages with custom templates:

```csharp
[LogMethod(LogLevel.Warning,
    EntryMessage = "Cancelling order {orderId} due to customer request",
    ExitMessage = "Order {orderId} cancelled successfully in {DurationMs}ms")]
public partial Task CancelOrderAsync(string orderId);

private async partial Task CancelOrderCore(string orderId)
{
    var order = await _repository.GetByIdAsync(orderId);
    order.Status = OrderStatus.Cancelled;
    await _repository.UpdateAsync(order);
}
```

#### 5. Controlling What Gets Logged

```csharp
// Minimal logging for high-frequency operations
[LogMethod(LogLevel.Trace,
    LogParameters = false,      // Don't log parameters
    LogDuration = false,        // Don't log duration
    CreateActivity = false)]    // Don't create tracing span
public partial Task RecordViewAsync(string productId);

// Log parameters but not large objects
[LogMethod(LogLevel.Information,
    LogParameters = false)]  // Skip large batch list
public partial Task ProcessBatchAsync(string customerId, List<Order> orders);

// Log everything including result
[LogMethod(LogLevel.Debug,
    LogResult = true)]  // Include return value in log
public partial Task<string> GenerateReportAsync(string reportId);
```

#### 6. Different Log Levels

```csharp
// Information for business operations
[LogMethod(LogLevel.Information)]
public partial Task CreateOrderAsync(string orderId);

// Trace for queries (reduce log noise)
[LogMethod(LogLevel.Trace)]
public partial Task<Order?> GetOrderAsync(string orderId);

// Warning for operations that might need attention
[LogMethod(LogLevel.Warning)]
public partial Task RetryFailedOrderAsync(string orderId);

// Error for critical operations
[LogMethod(LogLevel.Error)]
public partial Task HandleCriticalFailureAsync(string orderId);
```

### Benefits

**Before (Manual Logging):**

```csharp
public async Task<Order> CreateOrderAsync(string orderId, decimal amount)
{
    using var activity = Activity.Current?.Source.StartActivity("CreateOrder");
    var stopwatch = Stopwatch.StartNew();

    _logger.LogInformation("Creating order {OrderId} with amount {Amount}", orderId, amount);

    try
    {
        var order = new Order { OrderId = orderId, Amount = amount };
        await _repository.SaveAsync(order);

        stopwatch.Stop();
        _logger.LogInformation("Created order {OrderId} in {DurationMs}ms",
            orderId, stopwatch.ElapsedMilliseconds);

        return order;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        _logger.LogError(ex, "Failed creating order {OrderId} after {DurationMs}ms",
            orderId, stopwatch.ElapsedMilliseconds);
        throw;
    }
}
```

**After (Generated):**

```csharp
[LogMethod(LogLevel.Information)]
public partial Task<Order> CreateOrderAsync(string orderId, decimal amount);

private async partial Task<Order> CreateOrderCore(string orderId, decimal amount)
{
    var order = new Order { OrderId = orderId, Amount = amount };
    await _repository.SaveAsync(order);
    return order;
}
```

**Savings:** 90% less code, consistent logging pattern across all methods.

---

## Metrics Instrumentation Generator

Automatically generates OpenTelemetry metrics instrumentation for methods with counters, histograms, and tags.

### Step-by-Step Usage

#### 1. Enable Metrics for a Class

```csharp
using HeroMessaging.SourceGenerators;
using System.Diagnostics.Metrics;

namespace MyApp.Services;

[GenerateMetrics(MeterName = "MyApp.OrderService")]
public partial class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }
}
```

#### 2. Instrument a Method

```csharp
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
    MetricName = "orders.processed",
    Description = "Order processing metrics")]
public partial Task<Order> ProcessOrderAsync(string orderId, decimal amount);

private async partial Task<Order> ProcessOrderCore(string orderId, decimal amount)
{
    var order = new Order { OrderId = orderId, Amount = amount };
    await _repository.SaveAsync(order);
    return order;
}
```

#### 3. Generated Metrics Infrastructure

```csharp
// Generated: OrderService.Metrics.g.cs
public partial class OrderService
{
    private static readonly Meter _meter = new Meter("MyApp.OrderService", "1.0.0");

    private static readonly Counter<long> _methodCallsCounter =
        _meter.CreateCounter<long>("method.calls", "count", "Total method calls");

    private static readonly Counter<long> _methodErrorsCounter =
        _meter.CreateCounter<long>("method.errors", "count", "Total method errors");

    private static readonly Histogram<double> _methodDurationHistogram =
        _meter.CreateHistogram<double>("method.duration", "ms", "Method execution duration");
}
```

#### 4. Generated Method Instrumentation

```csharp
// Generated: OrderService.ProcessOrderAsync.Metrics.g.cs
public partial class OrderService
{
    public async partial Task<Order> ProcessOrderAsync(string orderId, decimal amount)
    {
        var tags = new TagList
        {
            { "method", "ProcessOrderAsync" },
            { "class", "OrderService" }
        };

        var stopwatch = Stopwatch.StartNew();
        _methodCallsCounter.Add(1, tags);

        try
        {
            var result = await ProcessOrderCore(orderId, amount);

            stopwatch.Stop();
            tags.Add("status", "success");
            _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            tags.Add("status", "error");
            tags.Add("error_type", ex.GetType().Name);

            _methodErrorsCounter.Add(1, tags);
            _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);

            throw;
        }
    }
}
```

#### 5. Controlling Metric Cardinality with Tags

Use `[MetricTag]` to explicitly choose which parameters become metric dimensions:

```csharp
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
    MetricName = "payments.processed")]
public partial Task<PaymentResult> ProcessPaymentAsync(
    [MetricTag] string paymentMethod,    // Becomes metric tag (low cardinality)
    [MetricTag] string currency,         // Becomes metric tag
    string orderId,                       // NOT a tag (high cardinality)
    decimal amount);                      // NOT a tag (continuous value)

// Generated tags:
// { "method", "ProcessPaymentAsync" }
// { "payment_method", "credit_card" }
// { "currency", "USD" }
// { "status", "success" }
```

#### 6. Different Instrumentation Types

**Counter Only** (for simple counting):

```csharp
[InstrumentMethod(InstrumentationType.Counter,
    MetricName = "orders.viewed")]
public partial Task RecordOrderViewAsync(string orderId);

// Generates counter only, no histogram
```

**Histogram Only** (for duration/size measurements):

```csharp
[InstrumentMethod(InstrumentationType.Histogram,
    MetricName = "query.duration",
    Unit = "ms")]
public partial Task<List<Order>> SearchOrdersAsync(string query);

// Generates histogram only, no counter
```

**All Metrics** (counter + histogram + gauge):

```csharp
[InstrumentMethod(InstrumentationType.All,
    MetricName = "cache.operations")]
public partial Task UpdateCacheAsync(string key, object value);

// Generates all metric types
```

#### 7. Custom Metric Names and Descriptions

```csharp
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
    MetricName = "inventory.reserved",          // Custom name
    Description = "Inventory reservation operations",
    Unit = "items")]                            // Custom unit
public partial Task<int> ReserveInventoryAsync(
    [MetricTag] string warehouseId,
    [MetricTag] string productId,
    int quantity);
```

#### 8. Combining with Logging

```csharp
// Use both generators together
[LogMethod(LogLevel.Information)]
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram,
    MetricName = "orders.processed")]
public partial Task<Order> ProcessOrderAsync(string orderId, decimal amount);

private async partial Task<Order> ProcessOrderCore(string orderId, decimal amount)
{
    // Your business logic here
    var order = new Order { OrderId = orderId, Amount = amount };
    await _repository.SaveAsync(order);
    return order;
}

// Generated code includes BOTH logging and metrics instrumentation
```

### Observability Integration

#### OpenTelemetry Setup

```csharp
// Program.cs
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("MyApp.OrderService");  // Match MeterName in [GenerateMetrics]
        metrics.AddPrometheusExporter();
        metrics.AddOtlpExporter();
    });
```

#### Querying Metrics

```promql
# Prometheus/Grafana queries

# Total orders processed
sum(rate(method_calls{method="ProcessOrderAsync"}[5m]))

# Error rate
sum(rate(method_errors{method="ProcessOrderAsync"}[5m])) /
sum(rate(method_calls{method="ProcessOrderAsync"}[5m]))

# P95 latency
histogram_quantile(0.95, method_duration{method="ProcessOrderAsync"})

# Requests by payment method
sum by (payment_method) (method_calls{method="ProcessPaymentAsync"})
```

### Benefits

**Before (Manual Metrics):**

```csharp
public async Task<Order> ProcessOrderAsync(string orderId, decimal amount)
{
    var tags = new TagList
    {
        { "method", "ProcessOrder" },
        { "class", "OrderService" }
    };

    var stopwatch = Stopwatch.StartNew();
    _methodCallsCounter.Add(1, tags);

    try
    {
        var order = new Order { OrderId = orderId, Amount = amount };
        await _repository.SaveAsync(order);

        stopwatch.Stop();
        tags.Add("status", "success");
        _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);

        return order;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        tags.Add("status", "error");
        tags.Add("error_type", ex.GetType().Name);

        _methodErrorsCounter.Add(1, tags);
        _methodDurationHistogram.Record(stopwatch.ElapsedMilliseconds, tags);

        throw;
    }
}
```

**After (Generated):**

```csharp
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram)]
public partial Task<Order> ProcessOrderAsync(string orderId, decimal amount);

private async partial Task<Order> ProcessOrderCore(string orderId, decimal amount)
{
    var order = new Order { OrderId = orderId, Amount = amount };
    await _repository.SaveAsync(order);
    return order;
}
```

**Savings:** 85% less code, consistent metrics across all methods, automatic error tracking.

---

## Contract Testing Generator

Automatically generates contract tests for messages to ensure backward compatibility and prevent breaking changes.

### Step-by-Step Usage

#### 1. Mark Message with Attribute

```csharp
using HeroMessaging.SourceGenerators;

namespace MyApp.Messages;

[GenerateContractTests(Version = "v1.0")]
[ContractVersion("v1.0", ChangeDescription = "Initial version")]
public record OrderCreatedEvent
{
    [ContractRequired]
    public string OrderId { get; init; } = string.Empty;

    [ContractRequired]
    public string CustomerId { get; init; } = string.Empty;

    [ContractRequired]
    public decimal TotalAmount { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Sample for testing
    [ContractSample("ValidOrder")]
    public static OrderCreatedEvent ValidSample() => new()
    {
        OrderId = "ORD-12345",
        CustomerId = "CUST-999",
        TotalAmount = 299.99m,
        CreatedAt = DateTime.UtcNow
    };
}
```

#### 2. Generated Contract Tests

```csharp
// Generated: OrderCreatedEvent.ContractTests.g.cs
public class OrderCreatedEventContractTests
{
    // Schema Snapshot Test
    [Fact]
    public void OrderCreatedEvent_SchemaSnapshot_HasNotChanged()
    {
        // Verifies properties haven't been added/removed/renamed/retyped
        var expectedProperties = new[]
        {
            ("OrderId", typeof(string)),
            ("CustomerId", typeof(string)),
            ("TotalAmount", typeof(decimal)),
            ("CreatedAt", typeof(DateTime))
        };

        var actualProperties = typeof(OrderCreatedEvent)
            .GetProperties()
            .Select(p => (p.Name, p.PropertyType))
            .OrderBy(p => p.Name)
            .ToArray();

        Assert.Equal(expectedProperties, actualProperties);
        // Test FAILS if schema changes!
    }

    // Required Properties Test
    [Fact]
    public void OrderCreatedEvent_RequiredProperties_ArePresent()
    {
        // Breaking change: Required properties must not be removed
        Assert.NotNull(typeof(OrderCreatedEvent).GetProperty("OrderId"));
        Assert.NotNull(typeof(OrderCreatedEvent).GetProperty("CustomerId"));
        Assert.NotNull(typeof(OrderCreatedEvent).GetProperty("TotalAmount"));
    }

    // Roundtrip Serialization Test
    [Fact]
    public void OrderCreatedEvent_ValidOrder_RoundtripSerialization_Succeeds()
    {
        var original = OrderCreatedEvent.ValidSample();

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

        Assert.Equal(original.OrderId, deserialized.OrderId);
        Assert.Equal(original.TotalAmount, deserialized.TotalAmount);
    }

    // Backward Compatibility Test
    [Fact]
    public void OrderCreatedEvent_CanDeserialize_MinimalValidJson()
    {
        // Old messages with just required fields should still work
        var minimalJson = @"{
          ""orderId"": ""ORD-123"",
          ""customerId"": ""CUST-456"",
          ""totalAmount"": 99.99
        }";

        var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent>(minimalJson);
        Assert.NotNull(deserialized);
    }

    // Forward Compatibility Test
    [Fact]
    public void OrderCreatedEvent_CanDeserialize_WithExtraFields()
    {
        // Should ignore fields from newer versions
        var jsonWithExtra = @"{
          ""orderId"": ""ORD-123"",
          ""customerId"": ""CUST-456"",
          ""totalAmount"": 99.99,
          ""futureField"": ""should-be-ignored""
        }";

        var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent>(jsonWithExtra);
        Assert.NotNull(deserialized);  // No exception!
    }
}
```

#### 3. Safe Schema Evolution (Non-Breaking)

**Adding Optional Fields (v1.0 → v1.1):**

```csharp
[GenerateContractTests(Version = "v1.1")]
[ContractVersion("v1.1",
    ChangeDescription = "Added optional ShippingAddress field")]
public record OrderCreatedEvent
{
    // Original required fields - MUST NOT REMOVE
    [ContractRequired]
    public string OrderId { get; init; } = string.Empty;

    [ContractRequired]
    public string CustomerId { get; init; } = string.Empty;

    [ContractRequired]
    public decimal TotalAmount { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // NEW: Optional field added in v1.1 (backward compatible)
    public string? ShippingAddress { get; init; }  // Old messages work without this

    [ContractSample("ValidOrder")]
    public static OrderCreatedEvent ValidSample() => new()
    {
        OrderId = "ORD-12345",
        CustomerId = "CUST-999",
        TotalAmount = 299.99m,
        ShippingAddress = "123 Main St"  // New field
    };
}

// Contract tests pass! Old v1.0 messages still deserialize correctly
```

#### 4. Deprecating Fields (v1.1 → v1.2)

```csharp
[GenerateContractTests(Version = "v1.2")]
[ContractVersion("v1.2",
    ChangeDescription = "Deprecated CustomerId, added CustomerReference")]
public record OrderCreatedEvent
{
    [ContractRequired]
    public string OrderId { get; init; } = string.Empty;

    // DEPRECATED: Use CustomerReference instead
    [ContractDeprecated(
        SinceVersion = "v1.2",
        Reason = "Replaced by CustomerReference",
        ReplacedBy = nameof(CustomerReference))]
    public string? CustomerId { get; init; }  // Made optional

    // NEW: Replacement for CustomerId
    public CustomerRef? CustomerReference { get; init; }

    [ContractRequired]
    public decimal TotalAmount { get; init; }

    [ContractSample("NewFormat")]
    public static OrderCreatedEvent NewFormatSample() => new()
    {
        OrderId = "ORD-12345",
        CustomerReference = new CustomerRef { Id = "CUST-999" },
        TotalAmount = 299.99m
    };

    [ContractSample("LegacyFormat")]
    public static OrderCreatedEvent LegacyFormatSample() => new()
    {
        OrderId = "ORD-12345",
        CustomerId = "CUST-999",  // Still supported!
        TotalAmount = 299.99m
    };
}
```

#### 5. Breaking Changes (v2.0)

```csharp
[GenerateContractTests(Version = "v2.0")]
[ContractVersion("v2.0",
    ChangeDescription = "BREAKING: Removed CustomerId, changed TotalAmount to Money type")]
[BreakingChangeRule("Removed deprecated CustomerId")]
[BreakingChangeRule("Changed TotalAmount from decimal to Money")]
public record OrderCreatedEvent
{
    [ContractRequired]
    public string OrderId { get; init; } = string.Empty;

    // CustomerId completely removed (breaking!)

    [ContractRequired]
    public CustomerRef CustomerReference { get; init; } = new();

    // Type changed (breaking!)
    [AllowTypeChange("decimal", "Money",
        Reason = "Upgraded to support multi-currency")]
    [ContractRequired]
    public Money TotalAmount { get; init; } = new();

    [ContractSample("ValidOrder")]
    public static OrderCreatedEvent ValidSample() => new()
    {
        OrderId = "ORD-12345",
        CustomerReference = new CustomerRef { Id = "CUST-999" },
        TotalAmount = new Money { Amount = 299.99m, Currency = "USD" }
    };
}

// Contract tests create NEW baseline for v2.0
// Old v1.x tests continue to exist for compatibility testing
```

#### 6. Multiple Samples

```csharp
[GenerateContractTests]
public record OrderCreatedEvent
{
    // ... properties ...

    [ContractSample("SmallOrder")]
    public static OrderCreatedEvent SmallSample() => new()
    {
        OrderId = "ORD-001",
        TotalAmount = 10.00m
    };

    [ContractSample("LargeOrder")]
    public static OrderCreatedEvent LargeSample() => new()
    {
        OrderId = "ORD-999",
        TotalAmount = 10000.00m
    };

    [ContractSample("MinimalOrder")]
    public static OrderCreatedEvent MinimalSample() => new()
    {
        OrderId = "ORD-MIN",
        TotalAmount = 0.01m
    };
}

// Generates separate roundtrip test for each sample
```

### What Contract Tests Verify

#### 1. Schema Stability
- Properties haven't been added/removed
- Property types haven't changed
- Property names haven't been renamed

#### 2. Required Fields
- Required properties are still present
- Tests fail if required field is removed

#### 3. Serialization
- Messages can be serialized to JSON
- Messages can be deserialized from JSON
- Roundtrip preserves all data

#### 4. Backward Compatibility
- Old messages (minimal fields) still deserialize
- New code can read old messages

#### 5. Forward Compatibility
- New messages (extra fields) don't break old code
- Old code ignores unknown fields

### Safe vs Breaking Changes

**✅ Safe Changes (Non-Breaking):**
- Add new optional property
- Deprecate property (but keep it)
- Add new enum value (at end)
- Widen type (int32 → int64) with `[AllowTypeChange]`
- Add new method/sample
- Change documentation

**❌ Breaking Changes (Require Major Version):**
- Remove property
- Rename property
- Change property type
- Make optional property required
- Remove enum value
- Change serialization format

### CI/CD Integration

**In GitHub Actions:**

```yaml
name: Contract Tests

on:
  pull_request:
    paths:
      - 'src/MyApp.Messages/**'

jobs:
  contract-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Run Contract Tests
        run: dotnet test --filter "FullyQualifiedName~ContractTests"

      - name: Check for Breaking Changes
        run: |
          # Fail if contract tests failed
          if [ $? -ne 0 ]; then
            echo "❌ Contract tests failed - BREAKING CHANGES detected!"
            echo "Review changes to message schemas carefully."
            exit 1
          fi
```

**In Pull Requests:**

```markdown
## Message Schema Changes

Contract Test Status: ✅ Passing (No breaking changes)

### Changes:
- Added optional `ShippingAddress` field to `OrderCreatedEvent`
- This is backward compatible - old messages still deserialize

### Verification:
- ✅ Schema snapshot test passed
- ✅ Roundtrip serialization passed
- ✅ Backward compatibility test passed
- ✅ Forward compatibility test passed
```

### Best Practices

#### 1. Always Mark Required Fields

```csharp
// Good - Explicitly marks required fields
[ContractRequired]
public string OrderId { get; init; }

// Bad - Contract test can't distinguish required vs optional
public string OrderId { get; init; }
```

#### 2. Provide Multiple Samples

```csharp
// Good - Multiple scenarios covered
[ContractSample("Valid")]
public static OrderCreatedEvent ValidSample() => ...

[ContractSample("Minimal")]
public static OrderCreatedEvent MinimalSample() => ...

[ContractSample("Edge")]
public static OrderCreatedEvent EdgeSample() => ...
```

#### 3. Version All Messages

```csharp
// Good - Tracks version history
[GenerateContractTests(Version = "v1.0")]
[ContractVersion("v1.0",
    ChangeDescription = "Initial version",
    IntroducedDate = "2025-11-08")]
```

#### 4. Document Breaking Changes

```csharp
// Good - Documents rules clearly
[BreakingChangeRule("Cannot remove OrderId property")]
[BreakingChangeRule("Cannot change TotalAmount type")]
public record OrderCreatedEvent { ... }
```

#### 5. Test in CI/CD

```bash
# Run only contract tests
dotnet test --filter "FullyQualifiedName~ContractTests"

# Fail build on contract breakage
dotnet test --filter "FullyQualifiedName~ContractTests" || exit 1
```

### Benefits

**Before (No Contract Testing):**
- Breaking changes discovered in production
- No way to verify backward compatibility
- Manual testing of serialization
- API evolution is risky
- No documentation of message schemas

**After (With Contract Testing):**
- Breaking changes caught in CI/CD
- Automatic backward compatibility verification
- Generated serialization tests
- Safe API evolution with version tracking
- Living documentation of contracts

**Savings:** Prevents production incidents, reduces testing time, documents APIs automatically.

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
3. **Sophisticated Test Data Builder Generator** - Advanced test builders with auto-randomization
4. **Idempotency Key Generator** - Deterministic deduplication keys
5. **Handler Registration Generator** - Auto-discovery of all handlers
6. **Saga DSL Generator** - Declarative state machine definitions
7. **Method Logging Generator** - Auto-generate entry/exit/duration/error logging
8. **Metrics Instrumentation Generator** - Auto-generate OpenTelemetry metrics
9. **Contract Testing Generator** - Auto-generate backward compatibility tests

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

// Sophisticated Test Data Building (with auto-randomization)
[GenerateTestDataBuilder]
public record Order
{
    [RandomString(Prefix = "ORD-", Length = 8)]
    public string OrderId { get; init; }

    [RandomDecimal(Min = 1.00, Max = 10000.00)]
    public decimal Amount { get; init; }
}
var order = TestData.Order().WithRandomData().Build();
var orders = TestData.Order().CreateMany(10);

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

// Logging
[LogMethod(LogLevel.Information)]
public partial Task<Order> ProcessOrderAsync(string orderId);
private async partial Task<Order> ProcessOrderCore(string orderId) { ... }

// Metrics
[GenerateMetrics(MeterName = "MyApp.OrderService")]
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram)]
public partial Task<Order> ProcessOrderAsync(string orderId);
private async partial Task<Order> ProcessOrderCore(string orderId) { ... }

// Combined (Logging + Metrics)
[LogMethod(LogLevel.Information)]
[InstrumentMethod(InstrumentationType.Counter | InstrumentationType.Histogram)]
public partial Task<Order> ProcessOrderAsync(string orderId);
private async partial Task<Order> ProcessOrderCore(string orderId) { ... }

// Contract Testing (backward compatibility)
[GenerateContractTests(Version = "v1.0")]
public record OrderCreatedEvent
{
    [ContractRequired]
    public string OrderId { get; init; }

    [ContractSample("Valid")]
    public static OrderCreatedEvent ValidSample() => ...
}
// Generates schema snapshot, roundtrip, and compatibility tests
```

For more details, see [README.md](README.md) and individual generator files in `Generators/`.
