// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;
using System.Collections.Generic;
using HeroMessaging.SourceGenerators;

namespace HeroMessaging.SourceGenerators.Examples;

/// <summary>
/// Example demonstrating contract testing for backward compatibility.
/// Contract tests ensure message schemas don't break consumers.
/// </summary>
public class ContractTestingExample
{
    // EXAMPLE 1: Basic contract testing with required fields

    /// <summary>
    /// v1.0 of OrderCreatedEvent - Initial version
    /// </summary>
    [GenerateContractTests(Version = "v1.0")]
    [ContractVersion("v1.0", ChangeDescription = "Initial version")]
    [BreakingChangeRule("Cannot remove or rename required properties")]
    [BreakingChangeRule("Cannot change property types")]
    public record OrderCreatedEvent_V1
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
        public static OrderCreatedEvent_V1 ValidSample() => new()
        {
            OrderId = "ORD-12345",
            CustomerId = "CUST-999",
            TotalAmount = 299.99m,
            CreatedAt = new DateTime(2025, 11, 8, 10, 30, 0, DateTimeKind.Utc)
        };

        [ContractSample("MinimalOrder")]
        public static OrderCreatedEvent_V1 MinimalSample() => new()
        {
            OrderId = "ORD-00001",
            CustomerId = "CUST-001",
            TotalAmount = 1.00m
        };
    }

    // EXAMPLE 2: Schema evolution - Adding new optional fields (safe!)

    /// <summary>
    /// v1.1 of OrderCreatedEvent - Added optional fields
    /// This is BACKWARD COMPATIBLE because new fields are optional
    /// </summary>
    [GenerateContractTests(Version = "v1.1")]
    [ContractVersion("v1.1",
        ChangeDescription = "Added optional ShippingAddress and Items fields",
        IntroducedDate = "2025-11-08")]
    public record OrderCreatedEvent_V1_1
    {
        // Original required fields - MUST NOT REMOVE
        [ContractRequired]
        public string OrderId { get; init; } = string.Empty;

        [ContractRequired]
        public string CustomerId { get; init; } = string.Empty;

        [ContractRequired]
        public decimal TotalAmount { get; init; }

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        // NEW: Optional fields added in v1.1 (backward compatible)
        public string? ShippingAddress { get; init; }  // Optional - old messages work without this

        public List<OrderItem>? Items { get; init; }  // Optional - old messages work without this

        [ContractSample("ValidOrder")]
        public static OrderCreatedEvent_V1_1 ValidSample() => new()
        {
            OrderId = "ORD-12345",
            CustomerId = "CUST-999",
            TotalAmount = 299.99m,
            CreatedAt = DateTime.UtcNow,
            ShippingAddress = "123 Main St, City, State 12345",
            Items = new List<OrderItem>
            {
                new() { ProductId = "PROD-1", Quantity = 2, Price = 149.99m }
            }
        };
    }

    // EXAMPLE 3: Deprecating fields (safe with warnings)

    /// <summary>
    /// v1.2 of OrderCreatedEvent - Deprecated old field, added new one
    /// </summary>
    [GenerateContractTests(Version = "v1.2")]
    [ContractVersion("v1.2",
        ChangeDescription = "Deprecated CustomerId, added CustomerReference")]
    public record OrderCreatedEvent_V1_2
    {
        [ContractRequired]
        public string OrderId { get; init; } = string.Empty;

        // DEPRECATED: Use CustomerReference instead
        [ContractDeprecated(
            SinceVersion = "v1.2",
            Reason = "Replaced by CustomerReference for better type safety",
            ReplacedBy = nameof(CustomerReference))]
        public string? CustomerId { get; init; }  // Made optional to allow new messages

        // NEW: Replacement for CustomerId
        public CustomerRef? CustomerReference { get; init; }

        [ContractRequired]
        public decimal TotalAmount { get; init; }

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        [ContractSample("NewFormat")]
        public static OrderCreatedEvent_V1_2 NewFormatSample() => new()
        {
            OrderId = "ORD-12345",
            CustomerReference = new CustomerRef { Id = "CUST-999", Type = "Premium" },
            TotalAmount = 299.99m
        };

        [ContractSample("LegacyFormat")]
        public static OrderCreatedEvent_V1_2 LegacyFormatSample() => new()
        {
            OrderId = "ORD-12345",
            CustomerId = "CUST-999",  // Still supported for backward compatibility
            TotalAmount = 299.99m
        };
    }

    // EXAMPLE 4: Breaking changes (documented and versioned)

    /// <summary>
    /// v2.0 of OrderCreatedEvent - BREAKING CHANGES
    /// This would be a major version bump
    /// </summary>
    [GenerateContractTests(Version = "v2.0")]
    [ContractVersion("v2.0",
        ChangeDescription = "BREAKING: Removed CustomerId, changed TotalAmount to Currency type")]
    [BreakingChangeRule("Removed deprecated CustomerId (use CustomerReference)")]
    [BreakingChangeRule("Changed TotalAmount from decimal to Money type")]
    public record OrderCreatedEvent_V2
    {
        [ContractRequired]
        public string OrderId { get; init; } = string.Empty;

        // CustomerId completely removed (breaking!)

        [ContractRequired]
        public CustomerRef CustomerReference { get; init; } = new();

        // Type changed from decimal to Money (breaking!)
        [AllowTypeChange("decimal", "Money",
            Reason = "Upgraded to support multi-currency with proper type")]
        [ContractRequired]
        public Money TotalAmount { get; init; } = new();

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        [ContractSample("ValidOrder")]
        public static OrderCreatedEvent_V2 ValidSample() => new()
        {
            OrderId = "ORD-12345",
            CustomerReference = new CustomerRef { Id = "CUST-999", Type = "Premium" },
            TotalAmount = new Money { Amount = 299.99m, Currency = "USD" }
        };
    }

    // EXAMPLE 5: Contract testing for queries

    [GenerateContractTests(Version = "v1.0")]
    public record GetOrderQuery
    {
        [ContractRequired]
        public string OrderId { get; init; } = string.Empty;

        public bool IncludeItems { get; init; } = false;

        [ContractSample("Simple")]
        public static GetOrderQuery SimpleSample() => new()
        {
            OrderId = "ORD-12345"
        };

        [ContractSample("WithItems")]
        public static GetOrderQuery WithItemsSample() => new()
        {
            OrderId = "ORD-12345",
            IncludeItems = true
        };
    }

    // EXAMPLE 6: Contract testing for responses

    [GenerateContractTests(Version = "v1.0")]
    public record OrderResponse
    {
        [ContractRequired]
        public string OrderId { get; init; } = string.Empty;

        [ContractRequired]
        public OrderStatus Status { get; init; }

        [ContractRequired]
        public decimal TotalAmount { get; init; }

        public List<OrderItem>? Items { get; init; }

        [ContractSample("CreatedOrder")]
        public static OrderResponse CreatedSample() => new()
        {
            OrderId = "ORD-12345",
            Status = OrderStatus.Created,
            TotalAmount = 299.99m,
            Items = new List<OrderItem>
            {
                new() { ProductId = "PROD-1", Quantity = 2, Price = 149.99m }
            }
        };

        [ContractSample("MinimalResponse")]
        public static OrderResponse MinimalSample() => new()
        {
            OrderId = "ORD-12345",
            Status = OrderStatus.Created,
            TotalAmount = 0m
        };
    }

    // EXAMPLE 7: Nested object contracts

    [GenerateContractTests(Version = "v1.0")]
    public record OrderItem
    {
        [ContractRequired]
        public string ProductId { get; init; } = string.Empty;

        [ContractRequired]
        public int Quantity { get; init; }

        [ContractRequired]
        public decimal Price { get; init; }

        [ContractSample("StandardItem")]
        public static OrderItem StandardSample() => new()
        {
            ProductId = "PROD-123",
            Quantity = 2,
            Price = 49.99m
        };
    }
}

// Supporting types for examples
public record CustomerRef
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

public record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
}

public enum OrderStatus
{
    Created,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}

/*
 * GENERATED CONTRACT TESTS (example for OrderCreatedEvent_V1):
 *
 * public class OrderCreatedEvent_V1ContractTests
 * {
 *     // SCHEMA SNAPSHOT TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_SchemaSnapshot_HasNotChanged()
 *     {
 *         // Verifies properties haven't been added/removed/renamed/retyped
 *         var expectedProperties = new[]
 *         {
 *             ("OrderId", typeof(string)),
 *             ("CustomerId", typeof(string)),
 *             ("TotalAmount", typeof(decimal)),
 *             ("CreatedAt", typeof(DateTime))
 *         };
 *
 *         var actualProperties = typeof(OrderCreatedEvent_V1)
 *             .GetProperties()
 *             .Select(p => (p.Name, p.PropertyType))
 *             .OrderBy(p => p.Name)
 *             .ToArray();
 *
 *         Assert.Equal(expectedProperties.Length, actualProperties.Length);
 *         // Fails if schema changed!
 *     }
 *
 *     // REQUIRED PROPERTIES TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_RequiredProperties_ArePresent()
 *     {
 *         // Breaking change: Required properties must not be removed
 *         Assert.NotNull(typeof(OrderCreatedEvent_V1).GetProperty("OrderId"));
 *         Assert.NotNull(typeof(OrderCreatedEvent_V1).GetProperty("CustomerId"));
 *         Assert.NotNull(typeof(OrderCreatedEvent_V1).GetProperty("TotalAmount"));
 *     }
 *
 *     // ROUNDTRIP SERIALIZATION TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_ValidOrder_RoundtripSerialization_Succeeds()
 *     {
 *         var original = OrderCreatedEvent_V1.ValidSample();
 *
 *         var json = JsonSerializer.Serialize(original);
 *         var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent_V1>(json);
 *
 *         Assert.Equal(original.OrderId, deserialized.OrderId);
 *         Assert.Equal(original.CustomerId, deserialized.CustomerId);
 *         Assert.Equal(original.TotalAmount, deserialized.TotalAmount);
 *     }
 *
 *     // BACKWARD COMPATIBILITY TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_CanDeserialize_MinimalValidJson()
 *     {
 *         // Old messages with just required fields should still work
 *         var minimalJson = @"{
 *           ""orderId"": """",
 *           ""customerId"": """",
 *           ""totalAmount"": 0.0
 *         }";
 *
 *         var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent_V1>(minimalJson);
 *         Assert.NotNull(deserialized);
 *     }
 *
 *     // FORWARD COMPATIBILITY TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_CanDeserialize_WithExtraFields()
 *     {
 *         // Should ignore fields from newer versions
 *         var jsonWithExtra = @"{
 *           ""orderId"": ""ORD-123"",
 *           ""customerId"": ""CUST-456"",
 *           ""totalAmount"": 99.99,
 *           ""futureField"": ""should-be-ignored""
 *         }";
 *
 *         var deserialized = JsonSerializer.Deserialize<OrderCreatedEvent_V1>(jsonWithExtra);
 *         Assert.NotNull(deserialized);  // No exception!
 *     }
 *
 *     // JSON SCHEMA TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_SerializedJson_HasExpectedStructure()
 *     {
 *         var message = OrderCreatedEvent_V1.ValidSample();
 *         var json = JsonSerializer.Serialize(message);
 *         using var document = JsonDocument.Parse(json);
 *
 *         Assert.True(document.RootElement.TryGetProperty("orderId", out _));
 *         Assert.True(document.RootElement.TryGetProperty("customerId", out _));
 *         Assert.True(document.RootElement.TryGetProperty("totalAmount", out _));
 *     }
 *
 *     // VERSION TEST
 *     [Fact]
 *     public void OrderCreatedEvent_V1_ContractVersion_IsDocumented()
 *     {
 *         const string currentVersion = "v1.0";
 *         Assert.Equal("v1.0", currentVersion);
 *     }
 * }
 *
 * BENEFITS OF CONTRACT TESTING:
 *
 * 1. **Prevents Breaking Changes**
 *    - Tests fail if you remove required properties
 *    - Tests fail if you change property types
 *    - Tests fail if you rename properties
 *
 * 2. **Documents API Contract**
 *    - Generated tests serve as living documentation
 *    - Shows exactly what fields are required
 *    - Tracks version history
 *
 * 3. **Enables Safe Evolution**
 *    - Add new optional fields safely (v1.0 -> v1.1)
 *    - Deprecate fields with warnings (v1.1 -> v1.2)
 *    - Plan breaking changes explicitly (v2.0)
 *
 * 4. **CI/CD Integration**
 *    - Run in CI to catch accidental breaking changes
 *    - Fail build if contract breaks
 *    - Review contract changes in PRs
 *
 * 5. **Backward Compatibility**
 *    - Old clients can still deserialize new messages (forward compat)
 *    - New clients can still deserialize old messages (backward compat)
 *    - Tests verify both directions
 *
 * SAFE CHANGES (Non-Breaking):
 * ✅ Add new optional property
 * ✅ Add new method/sample
 * ✅ Deprecate property (but keep it)
 * ✅ Widen type (int32 -> int64) with [AllowTypeChange]
 * ✅ Add new enum value (at end)
 *
 * BREAKING CHANGES (Require Major Version):
 * ❌ Remove property
 * ❌ Rename property
 * ❌ Change property type
 * ❌ Make optional property required
 * ❌ Remove enum value
 * ❌ Change serialization format
 *
 * WORKFLOW:
 *
 * 1. Define v1.0 message with [GenerateContractTests]
 * 2. Mark required fields with [ContractRequired]
 * 3. Add samples with [ContractSample]
 * 4. Run tests - they create baseline
 * 5. Commit generated tests as source of truth
 *
 * 6. When evolving schema:
 *    - Add optional fields = v1.1 (safe)
 *    - Deprecate fields = v1.2 (safe with warnings)
 *    - Remove/rename fields = v2.0 (breaking, new tests)
 *
 * 7. In CI/CD:
 *    - Contract tests run on every commit
 *    - Fail if breaking changes detected
 *    - Require manual version bump for breaking changes
 */
