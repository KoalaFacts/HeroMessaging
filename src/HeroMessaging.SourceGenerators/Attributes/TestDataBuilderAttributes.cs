// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;

namespace HeroMessaging.SourceGenerators;

/// <summary>
/// Generates sophisticated test data builders with auto-randomization, object mothers, and collections.
/// More advanced than basic [GenerateBuilder] with realistic fake data generation.
/// </summary>
/// <example>
/// <code>
/// [GenerateTestDataBuilder]
/// public record Order
/// {
///     public string OrderId { get; init; } = string.Empty;
///     public string CustomerId { get; init; } = string.Empty;
///     public decimal Amount { get; init; }
///     public List&lt;OrderItem&gt; Items { get; init; } = new();
/// }
///
/// // Generated builder with auto-randomization:
/// var order = TestData.Order()
///     .WithRandomData()              // Fills all properties with realistic random values
///     .Build();
///
/// var orders = TestData.Order()
///     .CreateMany(10);                // Creates 10 random orders
///
/// var validOrder = TestData.ValidOrder();  // Predefined "valid" scenario
/// var expensiveOrder = TestData.ExpensiveOrder();  // Custom scenario
///
/// var order = TestData.Order()
///     .With(x => x.Amount, 999.99m)  // Lambda customization
///     .Build();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class GenerateTestDataBuilderAttribute : Attribute
{
    /// <summary>
    /// Whether to generate collection creation methods (CreateMany, CreateList).
    /// </summary>
    public bool GenerateCollections { get; set; } = true;

    /// <summary>
    /// Whether to generate lambda-based customization methods (With, For).
    /// </summary>
    public bool GenerateLambdaCustomization { get; set; } = true;

    /// <summary>
    /// Whether to generate auto-randomization support.
    /// </summary>
    public bool GenerateRandomization { get; set; } = true;

    /// <summary>
    /// Whether to generate Object Mother patterns (predefined scenarios).
    /// </summary>
    public bool GenerateObjectMothers { get; set; } = true;

    /// <summary>
    /// Prefix for the generated test data class (e.g., "TestData" -> TestData.Order()).
    /// </summary>
    public string TestDataClassName { get; set; } = "TestData";
}

/// <summary>
/// Defines a named builder scenario (Object Mother pattern).
/// Creates a predefined builder configuration for common test cases.
/// </summary>
/// <example>
/// <code>
/// [GenerateTestDataBuilder]
/// public record Order
/// {
///     [BuilderScenario("Valid")]
///     public static Order ValidScenario() => new()
///     {
///         OrderId = "ORD-12345",
///         Amount = 99.99m,
///         Status = OrderStatus.Created
///     };
///
///     [BuilderScenario("Expensive")]
///     public static Order ExpensiveScenario() => new()
///     {
///         OrderId = "ORD-99999",
///         Amount = 10000.00m,
///         Status = OrderStatus.Created
///     };
/// }
///
/// // Generated usage:
/// var validOrder = TestData.ValidOrder().Build();
/// var expensiveOrder = TestData.ExpensiveOrder().Build();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BuilderScenarioAttribute : Attribute
{
    /// <summary>
    /// Name of the scenario (e.g., "Valid", "Invalid", "Expensive").
    /// </summary>
    public string ScenarioName { get; }

    public BuilderScenarioAttribute(string scenarioName)
    {
        ScenarioName = scenarioName;
    }
}

/// <summary>
/// Configures how a property should be randomized in test data.
/// </summary>
/// <example>
/// <code>
/// [GenerateTestDataBuilder]
/// public record Order
/// {
///     [RandomString(Prefix = "ORD-", Length = 10)]
///     public string OrderId { get; init; } = string.Empty;
///
///     [RandomDecimal(Min = 1.00, Max = 1000.00)]
///     public decimal Amount { get; init; }
///
///     [RandomInt(Min = 1, Max = 100)]
///     public int Quantity { get; init; }
///
///     [RandomEmail]
///     public string CustomerEmail { get; init; } = string.Empty;
///
///     [RandomDateTime(DaysFromNow = -30, DaysToNow = 0)]  // Past 30 days
///     public DateTime OrderDate { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public abstract class RandomDataAttribute : Attribute
{
}

/// <summary>
/// Generates random strings with optional prefix, suffix, and length.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomStringAttribute : RandomDataAttribute
{
    /// <summary>
    /// Prefix for generated strings (e.g., "ORD-" -> "ORD-ABC123").
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Suffix for generated strings.
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>
    /// Length of the random part (default: 10).
    /// </summary>
    public int Length { get; set; } = 10;

    /// <summary>
    /// Character set to use (Alphanumeric, Alphabetic, Numeric, Hex).
    /// </summary>
    public RandomStringCharSet CharSet { get; set; } = RandomStringCharSet.Alphanumeric;
}

/// <summary>
/// Character sets for random string generation.
/// </summary>
public enum RandomStringCharSet
{
    Alphanumeric,
    Alphabetic,
    Numeric,
    Hex,
    AlphabeticLowercase,
    AlphabeticUppercase
}

/// <summary>
/// Generates random email addresses.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomEmailAttribute : RandomDataAttribute
{
    /// <summary>
    /// Domain for generated emails (default: "example.com").
    /// </summary>
    public string Domain { get; set; } = "example.com";
}

/// <summary>
/// Generates random integers within a range.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomIntAttribute : RandomDataAttribute
{
    /// <summary>
    /// Minimum value (inclusive).
    /// </summary>
    public int Min { get; set; } = 1;

    /// <summary>
    /// Maximum value (inclusive).
    /// </summary>
    public int Max { get; set; } = 100;
}

/// <summary>
/// Generates random decimals within a range.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomDecimalAttribute : RandomDataAttribute
{
    /// <summary>
    /// Minimum value (inclusive).
    /// </summary>
    public double Min { get; set; } = 0.01;

    /// <summary>
    /// Maximum value (inclusive).
    /// </summary>
    public double Max { get; set; } = 1000.00;

    /// <summary>
    /// Number of decimal places (default: 2).
    /// </summary>
    public int DecimalPlaces { get; set; } = 2;
}

/// <summary>
/// Generates random DateTimes within a range.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomDateTimeAttribute : RandomDataAttribute
{
    /// <summary>
    /// Days from now (negative for past, positive for future).
    /// Default: -365 (one year ago).
    /// </summary>
    public int DaysFromNow { get; set; } = -365;

    /// <summary>
    /// Days to now (negative for past, positive for future).
    /// Default: 0 (today).
    /// </summary>
    public int DaysToNow { get; set; } = 0;

    /// <summary>
    /// Whether to use UTC (default: true).
    /// </summary>
    public bool UseUtc { get; set; } = true;
}

/// <summary>
/// Generates random boolean values.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomBoolAttribute : RandomDataAttribute
{
    /// <summary>
    /// Probability of true (0.0 to 1.0, default: 0.5).
    /// </summary>
    public double TrueProbability { get; set; } = 0.5;
}

/// <summary>
/// Generates random enum values.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomEnumAttribute : RandomDataAttribute
{
    /// <summary>
    /// Excluded values (comma-separated names).
    /// </summary>
    public string? Exclude { get; set; }
}

/// <summary>
/// Generates random GUIDs.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomGuidAttribute : RandomDataAttribute
{
    /// <summary>
    /// Format for GUID string (N, D, B, P, X). Default: D.
    /// </summary>
    public string Format { get; set; } = "D";
}

/// <summary>
/// Specifies that a collection property should be auto-populated with random items.
/// </summary>
/// <example>
/// <code>
/// [GenerateTestDataBuilder]
/// public record Order
/// {
///     [RandomCollection(MinCount = 1, MaxCount = 5)]
///     public List&lt;OrderItem&gt; Items { get; init; } = new();
/// }
///
/// // Generated builder automatically creates 1-5 random OrderItems
/// var order = TestData.Order().WithRandomData().Build();
/// // order.Items will have 1-5 items automatically
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class RandomCollectionAttribute : RandomDataAttribute
{
    /// <summary>
    /// Minimum number of items in collection (default: 1).
    /// </summary>
    public int MinCount { get; set; } = 1;

    /// <summary>
    /// Maximum number of items in collection (default: 5).
    /// </summary>
    public int MaxCount { get; set; } = 5;
}
