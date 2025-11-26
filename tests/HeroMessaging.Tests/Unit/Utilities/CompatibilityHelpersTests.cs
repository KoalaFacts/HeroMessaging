using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class CompatibilityHelpersTests
{
    #region ThrowIfNull Tests

    [Fact]
    public void ThrowIfNull_WithNullArgument_ThrowsArgumentNullException()
    {
        // Arrange
        object? nullObject = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CompatibilityHelpers.ThrowIfNull(nullObject, "testParam"));

        Assert.Equal("testParam", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_WithNonNullArgument_DoesNotThrow()
    {
        // Arrange
        var validObject = new object();

        // Act & Assert - Should not throw
        CompatibilityHelpers.ThrowIfNull(validObject, "testParam");
    }

    [Fact]
    public void ThrowIfNull_WithNullString_ThrowsArgumentNullException()
    {
        // Arrange
        string? nullString = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CompatibilityHelpers.ThrowIfNull(nullString, "stringParam"));

        Assert.Equal("stringParam", exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_WithValidString_DoesNotThrow()
    {
        // Arrange
        var validString = "test";

        // Act & Assert - Should not throw
        CompatibilityHelpers.ThrowIfNull(validString, "stringParam");
    }

    [Fact]
    public void ThrowIfNull_WithEmptyString_DoesNotThrow()
    {
        // Arrange
        var emptyString = string.Empty;

        // Act & Assert - Should not throw (empty is not null)
        CompatibilityHelpers.ThrowIfNull(emptyString, "stringParam");
    }

    #endregion

    #region FromResult Tests

    [Fact]
    public void FromResult_WithIntValue_ReturnsValueTask()
    {
        // Arrange
        var value = 42;

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompletedSuccessfully);
        Assert.Equal(value, result.Result);
    }

    [Fact]
    public void FromResult_WithStringValue_ReturnsValueTask()
    {
        // Arrange
        var value = "test";

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompletedSuccessfully);
        Assert.Equal(value, result.Result);
    }

    [Fact]
    public void FromResult_WithNullValue_ReturnsValueTaskWithNull()
    {
        // Arrange
        string? value = null;

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompletedSuccessfully);
        Assert.Null(result.Result);
    }

    [Fact]
    public void FromResult_WithComplexObject_ReturnsValueTask()
    {
        // Arrange
        var value = new TestObject { Id = 1, Name = "Test" };

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompletedSuccessfully);
        Assert.Equal(value.Id, result.Result.Id);
        Assert.Equal(value.Name, result.Result.Name);
    }

    #endregion

    #region SetCanceled Tests

    [Fact]
    public void SetCanceled_WithCancellationToken_SetsTcsAsCanceled()
    {
        // Arrange
        var tcs = new TaskCompletionSource<int>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        CompatibilityHelpers.SetCanceled(tcs, cts.Token);

        // Assert
        Assert.True(tcs.Task.IsCanceled);
    }

    [Fact]
    public void SetCanceled_WithNonCanceledToken_StillSetsTcsAsCanceled()
    {
        // Arrange
        var tcs = new TaskCompletionSource<int>();
        var cts = new CancellationTokenSource();

        // Act
        CompatibilityHelpers.SetCanceled(tcs, cts.Token);

        // Assert
        Assert.True(tcs.Task.IsCanceled);
    }

    [Fact]
    public void SetCanceled_WithStringType_SetsTcsAsCanceled()
    {
        // Arrange
        var tcs = new TaskCompletionSource<string>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        CompatibilityHelpers.SetCanceled(tcs, cts.Token);

        // Assert
        Assert.True(tcs.Task.IsCanceled);
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_WithMatchingSubstring_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "World";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithNonMatchingSubstring_ReturnsFalse()
    {
        // Arrange
        var text = "Hello World";
        var value = "Goodbye";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithCaseInsensitiveComparison_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "world";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithCaseSensitiveComparison_ReturnsFalse()
    {
        // Arrange
        var text = "Hello World";
        var value = "world";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithNullText_ReturnsFalse()
    {
        // Arrange
        string? text = null;
        var value = "World";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithEmptyText_ReturnsFalse()
    {
        // Arrange
        var text = string.Empty;
        var value = "World";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithEmptyValue_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = string.Empty;

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var text = "Hello";
        var value = "Hello";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithCurrentCultureComparison_WorksCorrectly()
    {
        // Arrange
        var text = "Hello World";
        var value = "World";

        // Act
        var result = CompatibilityHelpers.Contains(text, value, StringComparison.CurrentCulture);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Helper Classes

    public class TestObject
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    #endregion
}
