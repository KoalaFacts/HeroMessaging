using System.Collections.Immutable;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Tests.Processing;

[Trait("Category", "Unit")]
public class ProcessingContextTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithDefaults()
    {
        // Arrange & Act
        var context = new ProcessingContext();

        // Assert
        Assert.Equal(string.Empty, context.Component);
        Assert.Null(context.Handler);
        Assert.Null(context.HandlerType);
        Assert.Equal(0, context.RetryCount);
        Assert.Null(context.FirstFailureTime);
        Assert.NotNull(context.Metadata);
        Assert.Empty(context.Metadata);
    }

    [Fact]
    public void Constructor_WithComponent_SetsComponent()
    {
        // Arrange & Act
        var context = new ProcessingContext("TestComponent");

        // Assert
        Assert.Equal("TestComponent", context.Component);
        Assert.Empty(context.Metadata);
    }

    [Fact]
    public void Constructor_WithComponentAndMetadata_SetsBoth()
    {
        // Arrange
        var metadata = ImmutableDictionary<string, object>.Empty.Add("key", "value");

        // Act
        var context = new ProcessingContext("TestComponent", metadata);

        // Assert
        Assert.Equal("TestComponent", context.Component);
        Assert.Single(context.Metadata);
        Assert.Equal("value", context.Metadata["key"]);
    }

    [Fact]
    public void WithMetadata_AddsMetadata()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");

        // Act
        var updated = context.WithMetadata("key1", "value1");

        // Assert
        Assert.Empty(context.Metadata);
        Assert.Single(updated.Metadata);
        Assert.Equal("value1", updated.Metadata["key1"]);
    }

    [Fact]
    public void WithMetadata_MultipleKeys_AddsAll()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");

        // Act
        var updated = context
            .WithMetadata("key1", "value1")
            .WithMetadata("key2", 123)
            .WithMetadata("key3", true);

        // Assert
        Assert.Equal(3, updated.Metadata.Count);
        Assert.Equal("value1", updated.Metadata["key1"]);
        Assert.Equal(123, updated.Metadata["key2"]);
        Assert.Equal(true, updated.Metadata["key3"]);
    }

    [Fact]
    public void WithRetry_SetsRetryCount()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");

        // Act
        var updated = context.WithRetry(3);

        // Assert
        Assert.Equal(0, context.RetryCount);
        Assert.Equal(3, updated.RetryCount);
        Assert.Null(updated.FirstFailureTime);
    }

    [Fact]
    public void WithRetry_WithFirstFailureTime_SetsBoth()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");
        var failureTime = DateTimeOffset.UtcNow;

        // Act
        var updated = context.WithRetry(2, failureTime);

        // Assert
        Assert.Equal(2, updated.RetryCount);
        Assert.Equal(failureTime, updated.FirstFailureTime);
    }

    [Fact]
    public void WithRetry_PreservesExistingFirstFailureTime()
    {
        // Arrange
        var originalFailureTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var context = new ProcessingContext("TestComponent")
        {
            FirstFailureTime = originalFailureTime
        };

        // Act
        var updated = context.WithRetry(2);

        // Assert
        Assert.Equal(originalFailureTime, updated.FirstFailureTime);
    }

    [Fact]
    public void GetMetadata_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("key", 42);

        // Act
        var value = context.GetMetadata<int>("key");

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void GetMetadata_WithNonExistingKey_ReturnsDefault()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");

        // Act
        var value = context.GetMetadata<int>("nonexistent");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void GetMetadata_WithWrongType_ReturnsDefault()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("key", "string-value");

        // Act
        var value = context.GetMetadata<int>("key");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void GetMetadataReference_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var stringValue = "test-string";
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("key", stringValue);

        // Act
        var value = context.GetMetadataReference<string>("key");

        // Assert
        Assert.Equal(stringValue, value);
    }

    [Fact]
    public void GetMetadataReference_WithNonExistingKey_ReturnsNull()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent");

        // Act
        var value = context.GetMetadataReference<string>("nonexistent");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void GetMetadataReference_WithWrongType_ReturnsNull()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("key", 123);

        // Act
        var value = context.GetMetadataReference<string>("key");

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public void Handler_CanBeSet()
    {
        // Arrange
        var handler = new object();

        // Act
        var context = new ProcessingContext("TestComponent")
        {
            Handler = handler
        };

        // Assert
        Assert.Same(handler, context.Handler);
    }

    [Fact]
    public void HandlerType_CanBeSet()
    {
        // Arrange
        var handlerType = typeof(string);

        // Act
        var context = new ProcessingContext("TestComponent")
        {
            HandlerType = handlerType
        };

        // Assert
        Assert.Equal(handlerType, context.HandlerType);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new ProcessingContext("Original");

        // Act
        var modified = original with { Component = "Modified" };

        // Assert
        Assert.Equal("Original", original.Component);
        Assert.Equal("Modified", modified.Component);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var context1 = new ProcessingContext("Test") { RetryCount = 3 };
        var context2 = new ProcessingContext("Test") { RetryCount = 3 };

        // Act & Assert
        Assert.Equal(context1, context2);
    }

    [Fact]
    public void Metadata_IsImmutable()
    {
        // Arrange
        var context = new ProcessingContext("Test")
            .WithMetadata("key1", "value1");

        // Act
        var updated = context.WithMetadata("key2", "value2");

        // Assert
        Assert.Single(context.Metadata);
        Assert.Equal(2, updated.Metadata.Count);
    }

    [Fact]
    public void RetryScenario_TracksAttemptsAndTime()
    {
        // Arrange
        var firstFailure = DateTimeOffset.UtcNow;
        var context = new ProcessingContext("TestComponent");

        // Act - Simulate retries
        var attempt1 = context.WithRetry(1, firstFailure);
        var attempt2 = attempt1.WithRetry(2);
        var attempt3 = attempt2.WithRetry(3);

        // Assert
        Assert.Equal(1, attempt1.RetryCount);
        Assert.Equal(2, attempt2.RetryCount);
        Assert.Equal(3, attempt3.RetryCount);
        Assert.Equal(firstFailure, attempt1.FirstFailureTime);
        Assert.Equal(firstFailure, attempt2.FirstFailureTime);
        Assert.Equal(firstFailure, attempt3.FirstFailureTime);
    }

    [Fact]
    public void ComplexMetadata_VariousTypes()
    {
        // Arrange & Act
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("string", "value")
            .WithMetadata("int", 42)
            .WithMetadata("bool", true)
            .WithMetadata("guid", Guid.NewGuid())
            .WithMetadata("datetime", DateTimeOffset.UtcNow);

        // Assert
        Assert.Equal(5, context.Metadata.Count);
        Assert.IsType<string>(context.Metadata["string"]);
        Assert.IsType<int>(context.Metadata["int"]);
        Assert.IsType<bool>(context.Metadata["bool"]);
        Assert.IsType<Guid>(context.Metadata["guid"]);
        Assert.IsType<DateTimeOffset>(context.Metadata["datetime"]);
    }

    [Fact]
    public void GetMetadata_WithNullableStruct_WorksCorrectly()
    {
        // Arrange
        var context = new ProcessingContext("TestComponent")
            .WithMetadata("int-key", 42);

        // Act
        var value = context.GetMetadata<int>("int-key");
        var nullValue = context.GetMetadata<int>("missing-key");

        // Assert
        Assert.Equal(42, value);
        Assert.Null(nullValue);
    }

    [Fact]
    public void StructBehavior_IsValueType()
    {
        // Arrange
        var context1 = new ProcessingContext("Test");

        // Act
        var context2 = context1;

        // Assert
        Assert.Equal(context1.Component, context2.Component);
    }

    [Fact]
    public void DefaultStruct_HasEmptyValues()
    {
        // Arrange & Act
        var context = default(ProcessingContext);

        // Assert
        Assert.Null(context.Component);
        Assert.Null(context.Handler);
        Assert.Null(context.HandlerType);
        Assert.Equal(0, context.RetryCount);
        Assert.Null(context.FirstFailureTime);
        Assert.Equal(ImmutableDictionary<string, object>.Empty, context.Metadata);
    }

    [Fact]
    public void GetHashCode_WithEqualValues_ReturnsSameHash()
    {
        // Arrange
        var context1 = new ProcessingContext("Test");
        var context2 = new ProcessingContext("Test");

        // Act
        var hash1 = context1.GetHashCode();
        var hash2 = context2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }
}
