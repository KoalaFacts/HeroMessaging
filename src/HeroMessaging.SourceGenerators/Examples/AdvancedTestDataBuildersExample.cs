// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;
using System.Collections.Generic;
using HeroMessaging.SourceGenerators;

namespace HeroMessaging.SourceGenerators.Examples;

/// <summary>
/// Example demonstrating sophisticated test data builders with auto-randomization.
/// Shows the difference between basic builders and advanced test data builders.
/// </summary>
public class AdvancedTestDataBuildersExample
{
    public void Example_BasicUsage()
    {
        // BEFORE: Manual test data creation (tedious!)
        var order1Manual = new Order
        {
            OrderId = "ORD-12345",
            CustomerId = "CUST-999",
            CustomerEmail = "[email protected]",
            Amount = 299.99m,
            Quantity = 2,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-5),
            Status = OrderStatus.Created,
            Items = new List<OrderItem>
            {
                new() { ProductId = "PROD-1", Quantity = 1, UnitPrice = 149.99m },
                new() { ProductId = "PROD-2", Quantity = 1, UnitPrice = 150.00m }
            }
        };

        // AFTER: Using test data builder with auto-randomization
        var order2 = TestData.Order()
            .WithRandomData()  // Automatically fills all properties with realistic random values
            .Build();

        // Override specific fields while keeping randomized others
        var order3 = TestData.Order()
            .WithRandomData()
            .WithOrderId("ORD-SPECIFIC")  // Override just what you need
            .WithAmount(999.99m)
            .Build();
    }

    public void Example_Collections()
    {
        // Create multiple random orders easily
        var orders = TestData.Order()
            .CreateMany(10);  // 10 randomized orders

        // All orders have realistic random data:
        // - Unique Order IDs
        // - Random amounts
        // - Random dates
        // - etc.

        foreach (var order in orders)
        {
            Console.WriteLine($"Order: {order.OrderId}, Amount: {order.Amount:C}");
        }
    }

    public void Example_ObjectMothers()
    {
        // Predefined scenarios (when added via [BuilderScenario])
        // These would be generated if you add BuilderScenario methods

        // Example of what would be generated:
        // var validOrder = TestData.ValidOrder().Build();
        // var expensiveOrder = TestData.ExpensiveOrder().Build();
        // var internationalOrder = TestData.InternationalOrder().Build();
    }

    public void Example_InTests()
    {
        // In unit tests - create test data quickly

        // Test with random valid data
        var order = TestData.Order()
            .WithRandomData()
            .Build();

        Assert.NotNull(order);
        Assert.NotEmpty(order.OrderId);
        Assert.True(order.Amount > 0);

        // Test with specific edge case
        var largeOrder = TestData.Order()
            .WithRandomData()
            .WithAmount(999999.99m)  // Max amount
            .WithQuantity(1000)      // Max quantity
            .Build();

        Assert.Equal(999999.99m, largeOrder.Amount);

        // Test with multiple scenarios
        var orders = TestData.Order().CreateMany(100);

        Assert.Equal(100, orders.Count);
        Assert.True(orders.All(o => !string.IsNullOrEmpty(o.OrderId)));
    }

    public void Example_Fixtures()
    {
        // Create reusable test fixtures

        // Base order fixture
        var baseOrder = TestData.Order()
            .WithRandomData()
            .WithStatus(OrderStatus.Created)
            .Build();

        // Paid order fixture
        var paidOrder = TestData.Order()
            .WithRandomData()
            .WithStatus(OrderStatus.Paid)
            .WithAmount(100.00m)
            .Build();

        // Cancelled order fixture
        var cancelledOrder = TestData.Order()
            .WithRandomData()
            .WithStatus(OrderStatus.Cancelled)
            .Build();

        // Use in tests
        var processor = new OrderProcessor();
        var result = processor.Process(paidOrder);

        Assert.True(result.Success);
    }
}

// Example domain model with test data builder
[GenerateTestDataBuilder]
public record Order
{
    [RandomString(Prefix = "ORD-", Length = 8, CharSet = RandomStringCharSet.Alphanumeric)]
    public string OrderId { get; init; } = string.Empty;

    [RandomString(Prefix = "CUST-", Length = 6, CharSet = RandomStringCharSet.Numeric)]
    public string CustomerId { get; init; } = string.Empty;

    [RandomEmail(Domain = "example.com")]
    public string CustomerEmail { get; init; } = string.Empty;

    [RandomDecimal(Min = 1.00, Max = 10000.00, DecimalPlaces = 2)]
    public decimal Amount { get; init; }

    [RandomInt(Min = 1, Max = 100)]
    public int Quantity { get; init; }

    [RandomDateTime(DaysFromNow = -90, DaysToNow = 0)]  // Orders from past 90 days
    public DateTime OrderDate { get; init; } = DateTimeOffset.UtcNow;

    [RandomEnum]
    public OrderStatus Status { get; init; } = OrderStatus.Created;

    [RandomCollection(MinCount = 1, MaxCount = 5)]
    public List<OrderItem> Items { get; init; } = new();

    // Example of builder scenarios (object mother pattern)
    [BuilderScenario("Valid")]
    public static Order ValidScenario() => new()
    {
        OrderId = "ORD-VALID001",
        CustomerId = "CUST-12345",
        CustomerEmail = "[email protected]",
        Amount = 99.99m,
        Quantity = 1,
        OrderDate = DateTimeOffset.UtcNow,
        Status = OrderStatus.Created,
        Items = new List<OrderItem>
        {
            new() { ProductId = "PROD-1", Quantity = 1, UnitPrice = 99.99m }
        }
    };

    [BuilderScenario("Expensive")]
    public static Order ExpensiveScenario() => new()
    {
        OrderId = "ORD-EXPENSIVE",
        CustomerId = "CUST-VIP",
        CustomerEmail = "[email protected]",
        Amount = 9999.99m,
        Quantity = 10,
        OrderDate = DateTimeOffset.UtcNow,
        Status = OrderStatus.Created,
        Items = new List<OrderItem>
        {
            new() { ProductId = "PROD-LUXURY", Quantity = 10, UnitPrice = 999.99m }
        }
    };

    [BuilderScenario("Invalid")]
    public static Order InvalidScenario() => new()
    {
        OrderId = "",  // Invalid - empty ID
        CustomerId = "",
        CustomerEmail = "not-an-email",  // Invalid email
        Amount = -50.00m,  // Invalid - negative amount
        Quantity = 0,  // Invalid - zero quantity
        OrderDate = DateTime.MinValue,
        Status = OrderStatus.Created,
        Items = new()  // Invalid - no items
    };
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

public enum OrderStatus
{
    Created,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}

// Supporting classes for example
public class OrderProcessor
{
    public ProcessResult Process(Order order)
    {
        if (order.Amount <= 0)
            return new ProcessResult { Success = false };

        return new ProcessResult { Success = true };
    }
}

public class ProcessResult
{
    public bool Success { get; set; }
}

public static class Assert
{
    public static void NotNull(object obj) { }
    public static void NotEmpty(string str) { }
    public static void True(bool condition) { }
    public static void Equal<T>(T expected, T actual) { }
}

/*
 * GENERATED CODE (example for Order):
 *
 * public static partial class TestData
 * {
 *     private static readonly Random _random = new Random();
 *
 *     public static OrderBuilder Order() => new OrderBuilder();
 *
 *     public class OrderBuilder
 *     {
 *         private static int _sequence = 0;
 *         private string _orderId;
 *         private string _customerId;
 *         private string _customerEmail;
 *         private decimal _amount;
 *         private int _quantity;
 *         private DateTime _orderDate;
 *         private OrderStatus _status;
 *         private List<OrderItem> _items;
 *
 *         public OrderBuilder()
 *         {
 *             _sequence++;
 *             // Initialize defaults
 *         }
 *
 *         public OrderBuilder WithOrderId(string value)
 *         {
 *             _orderId = value;
 *             return this;
 *         }
 *
 *         // ... similar With methods for all properties ...
 *
 *         public OrderBuilder WithRandomData()
 *         {
 *             // Uses [RandomString] attribute
 *             _orderId = "ORD-" + GenerateRandomString(8);
 *
 *             // Uses [RandomString] attribute
 *             _customerId = "CUST-" + _random.Next(100000, 999999);
 *
 *             // Uses [RandomEmail] attribute
 *             _customerEmail = "user" + _random.Next(1000, 9999) + "@example.com";
 *
 *             // Uses [RandomDecimal] attribute
 *             _amount = Math.Round((decimal)(_random.NextDouble() * (10000.00 - 1.00) + 1.00), 2);
 *
 *             // Uses [RandomInt] attribute
 *             _quantity = _random.Next(1, 101);
 *
 *             // Uses [RandomDateTime] attribute
 *             _orderDate = DateTimeOffset.UtcNow.AddDays(_random.Next(-90, 1));
 *
 *             // Uses [RandomEnum] attribute
 *             _status = (OrderStatus)_random.Next(0, Enum.GetValues(typeof(OrderStatus)).Length);
 *
 *             // Uses [RandomCollection] attribute
 *             _items = new List<OrderItem>();
 *             var itemCount = _random.Next(1, 6);
 *             for (int i = 0; i < itemCount; i++)
 *             {
 *                 _items.Add(TestData.OrderItem().WithRandomData().Build());
 *             }
 *
 *             return this;
 *         }
 *
 *         public Order Build()
 *         {
 *             return new Order
 *             {
 *                 OrderId = _orderId,
 *                 CustomerId = _customerId,
 *                 CustomerEmail = _customerEmail,
 *                 Amount = _amount,
 *                 Quantity = _quantity,
 *                 OrderDate = _orderDate,
 *                 Status = _status,
 *                 Items = _items
 *             };
 *         }
 *
 *         public List<Order> CreateMany(int count)
 *         {
 *             var items = new List<Order>();
 *             for (int i = 0; i < count; i++)
 *             {
 *                 items.Add(new OrderBuilder().WithRandomData().Build());
 *             }
 *             return items;
 *         }
 *     }
 *
 *     // Object Mother methods (generated from [BuilderScenario])
 *     public static OrderBuilder ValidOrder()
 *     {
 *         var scenario = Order.ValidScenario();
 *         return Order()
 *             .WithOrderId(scenario.OrderId)
 *             .WithCustomerId(scenario.CustomerId)
 *             .WithCustomerEmail(scenario.CustomerEmail)
 *             .WithAmount(scenario.Amount)
 *             .WithQuantity(scenario.Quantity)
 *             .WithOrderDate(scenario.OrderDate)
 *             .WithStatus(scenario.Status)
 *             .WithItems(scenario.Items);
 *     }
 *
 *     public static OrderBuilder ExpensiveOrder()
 *     {
 *         var scenario = Order.ExpensiveScenario();
 *         return Order()
 *             .WithOrderId(scenario.OrderId)
 *             // ... etc
 *     }
 * }
 *
 * USAGE COMPARISON:
 *
 * // BEFORE (Manual):
 * var order = new Order
 * {
 *     OrderId = "ORD-12345",
 *     CustomerId = "CUST-999",
 *     CustomerEmail = "test@example.com",
 *     Amount = 99.99m,
 *     Quantity = 1,
 *     OrderDate = DateTimeOffset.UtcNow,
 *     Status = OrderStatus.Created,
 *     Items = new List<OrderItem>
 *     {
 *         new() { ProductId = "PROD-1", Quantity = 1, UnitPrice = 99.99m }
 *     }
 * };
 *
 * // AFTER (Generated Builder):
 * var order = TestData.Order().WithRandomData().Build();
 *
 * // Or with specific overrides:
 * var order = TestData.Order()
 *     .WithRandomData()
 *     .WithOrderId("ORD-SPECIFIC")
 *     .Build();
 *
 * // Or predefined scenario:
 * var order = TestData.ValidOrder().Build();
 *
 * // Or multiple:
 * var orders = TestData.Order().CreateMany(100);
 *
 * BENEFITS:
 * - 95% less code for test data
 * - Realistic random data automatically
 * - Easy to create variations
 * - Reusable test scenarios (Object Mother)
 * - Collections support built-in
 * - Type-safe fluent API
 * - No manual property assignment
 * - Consistent test data across test suite
 */
