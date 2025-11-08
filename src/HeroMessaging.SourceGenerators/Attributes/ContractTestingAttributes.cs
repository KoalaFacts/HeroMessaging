// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;

namespace HeroMessaging.SourceGenerators;

/// <summary>
/// Generates contract tests for a message to ensure backward compatibility.
/// Creates snapshot tests, schema validation, and roundtrip serialization tests.
/// </summary>
/// <example>
/// <code>
/// [GenerateContractTests(Version = "v1.0")]
/// public record OrderCreatedEvent
/// {
///     public string OrderId { get; init; }
///     public decimal Amount { get; init; }
/// }
///
/// // Generated tests verify:
/// // - Schema hasn't changed (property names, types)
/// // - Can deserialize old messages (backward compatibility)
/// // - Can serialize/deserialize roundtrip
/// // - JSON schema matches expectations
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class GenerateContractTestsAttribute : Attribute
{
    /// <summary>
    /// Version of this contract (e.g., "v1.0", "v2.0").
    /// Used to track schema evolution over time.
    /// </summary>
    public string Version { get; set; } = "v1.0";

    /// <summary>
    /// Whether to generate snapshot tests (default: true).
    /// Snapshot tests capture the current schema and fail if it changes.
    /// </summary>
    public bool GenerateSnapshotTests { get; set; } = true;

    /// <summary>
    /// Whether to generate roundtrip serialization tests (default: true).
    /// Verifies that messages can be serialized and deserialized without loss.
    /// </summary>
    public bool GenerateRoundtripTests { get; set; } = true;

    /// <summary>
    /// Whether to generate backward compatibility tests (default: true).
    /// Verifies that old message versions can still be deserialized.
    /// </summary>
    public bool GenerateBackwardCompatibilityTests { get; set; } = true;

    /// <summary>
    /// Whether to generate JSON schema validation (default: true).
    /// </summary>
    public bool GenerateSchemaValidation { get; set; } = true;

    /// <summary>
    /// Test framework to use (Xunit, NUnit, MSTest).
    /// </summary>
    public TestFramework Framework { get; set; } = TestFramework.Xunit;
}

/// <summary>
/// Supported test frameworks for contract tests.
/// </summary>
public enum TestFramework
{
    Xunit,
    NUnit,
    MSTest
}

/// <summary>
/// Marks a property as required in the contract.
/// Breaking change detection will fail if this property is removed.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ContractRequiredAttribute : Attribute
{
    /// <summary>
    /// Custom error message if this property is missing.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Marks a property as deprecated in the contract.
/// Warns if this property is used, but doesn't fail tests.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ContractDeprecatedAttribute : Attribute
{
    /// <summary>
    /// Version when this property was deprecated.
    /// </summary>
    public string? SinceVersion { get; set; }

    /// <summary>
    /// Reason for deprecation.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Replacement property name (if any).
    /// </summary>
    public string? ReplacedBy { get; set; }
}

/// <summary>
/// Defines a sample message instance for contract testing.
/// Used to create test fixtures and validate serialization.
/// </summary>
/// <example>
/// <code>
/// [GenerateContractTests]
/// public record OrderCreatedEvent
/// {
///     public string OrderId { get; init; }
///     public decimal Amount { get; init; }
///
///     [ContractSample("ValidOrder")]
///     public static OrderCreatedEvent ValidSample() => new()
///     {
///         OrderId = "ORD-12345",
///         Amount = 99.99m
///     };
///
///     [ContractSample("LargeOrder")]
///     public static OrderCreatedEvent LargeSample() => new()
///     {
///         OrderId = "ORD-99999",
///         Amount = 10000.00m
///     };
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ContractSampleAttribute : Attribute
{
    /// <summary>
    /// Name of this sample (e.g., "ValidOrder", "MinimalOrder").
    /// </summary>
    public string SampleName { get; }

    public ContractSampleAttribute(string sampleName)
    {
        SampleName = sampleName;
    }
}

/// <summary>
/// Specifies expected JSON representation for contract validation.
/// Used to ensure serialization format remains stable.
/// </summary>
/// <example>
/// <code>
/// [ExpectedJson(@"{
///   ""orderId"": ""ORD-12345"",
///   ""amount"": 99.99
/// }")]
/// public static OrderCreatedEvent ValidSample() => ...
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExpectedJsonAttribute : Attribute
{
    /// <summary>
    /// Expected JSON representation.
    /// </summary>
    public string Json { get; }

    /// <summary>
    /// Whether to ignore whitespace when comparing JSON.
    /// </summary>
    public bool IgnoreWhitespace { get; set; } = true;

    /// <summary>
    /// Whether to ignore property order when comparing JSON.
    /// </summary>
    public bool IgnoreOrder { get; set; } = true;

    public ExpectedJsonAttribute(string json)
    {
        Json = json;
    }
}

/// <summary>
/// Defines a breaking change rule for the contract.
/// Helps document what changes are considered breaking.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class BreakingChangeRuleAttribute : Attribute
{
    /// <summary>
    /// Description of the breaking change rule.
    /// </summary>
    public string Rule { get; }

    /// <summary>
    /// Examples of what would violate this rule.
    /// </summary>
    public string? Examples { get; set; }

    public BreakingChangeRuleAttribute(string rule)
    {
        Rule = rule;
    }
}

/// <summary>
/// Specifies that a property type change is allowed (not breaking).
/// Useful for widening types (e.g., int32 -> int64).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class AllowTypeChangeAttribute : Attribute
{
    /// <summary>
    /// Original type name.
    /// </summary>
    public string FromType { get; }

    /// <summary>
    /// New type name.
    /// </summary>
    public string ToType { get; }

    /// <summary>
    /// Reason this type change is safe.
    /// </summary>
    public string? Reason { get; set; }

    public AllowTypeChangeAttribute(string fromType, string toType)
    {
        FromType = fromType;
        ToType = toType;
    }
}

/// <summary>
/// Enables contract versioning for schema evolution.
/// Tracks changes across versions and validates migration paths.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ContractVersionAttribute : Attribute
{
    /// <summary>
    /// Version identifier (e.g., "v1.0", "v2.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Description of changes in this version.
    /// </summary>
    public string? ChangeDescription { get; set; }

    /// <summary>
    /// Date this version was introduced (ISO 8601).
    /// </summary>
    public string? IntroducedDate { get; set; }

    public ContractVersionAttribute(string version)
    {
        Version = version;
    }
}
