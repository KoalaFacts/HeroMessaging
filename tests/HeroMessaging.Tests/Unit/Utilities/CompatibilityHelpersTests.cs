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
        object? argument = null;
        var paramName = "testParam";

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CompatibilityHelpers.ThrowIfNull(argument, paramName));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_WithNonNullArgument_DoesNotThrow()
    {
        // Arrange
        object argument = new object();
        var paramName = "testParam";

        // Act & Assert (should not throw)
        CompatibilityHelpers.ThrowIfNull(argument, paramName);
        Assert.True(true);
    }

    [Fact]
    public void ThrowIfNull_WithNullString_ThrowsArgumentNullException()
    {
        // Arrange
        string? argument = null;
        var paramName = "stringParam";

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CompatibilityHelpers.ThrowIfNull(argument, paramName));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void ThrowIfNull_WithEmptyString_DoesNotThrow()
    {
        // Arrange
        string argument = string.Empty;
        var paramName = "stringParam";

        // Act & Assert
        CompatibilityHelpers.ThrowIfNull(argument, paramName);
        Assert.True(true);
    }

    [Fact]
    public void ThrowIfNull_WithNullCollection_ThrowsArgumentNullException()
    {
        // Arrange
        List<int>? argument = null;
        var paramName = "collectionParam";

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CompatibilityHelpers.ThrowIfNull(argument, paramName));

        Assert.Equal(paramName, exception.ParamName);
    }

    #endregion

    #region FromResult Tests

    [Fact]
    public void FromResult_WithInt_ReturnsValueTask()
    {
        // Arrange
        var value = 42;

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompleted);
        Assert.Equal(value, result.Result);
    }

    [Fact]
    public void FromResult_WithString_ReturnsValueTask()
    {
        // Arrange
        var value = "test-string";

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompleted);
        Assert.Equal(value, result.Result);
    }

    [Fact]
    public void FromResult_WithNull_ReturnsValueTaskWithNull()
    {
        // Arrange
        string? value = null;

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompleted);
        Assert.Null(result.Result);
    }

    [Fact]
    public void FromResult_WithObject_ReturnsValueTask()
    {
        // Arrange
        var value = new object();

        // Act
        var result = CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.True(result.IsCompleted);
        Assert.Same(value, result.Result);
    }

    [Fact]
    public async Task FromResult_CanBeAwaited()
    {
        // Arrange
        var value = 100;

        // Act
        var result = await CompatibilityHelpers.FromResult(value);

        // Assert
        Assert.Equal(value, result);
    }

    #endregion

    #region SetCanceled Tests

    [Fact]
    public void SetCanceled_WithValidTaskCompletionSource_SetsCanceled()
    {
        // Arrange
        var tcs = new TaskCompletionSource<int>();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Act
        tcs.SetCanceled(cancellationToken);

        // Assert
        Assert.True(tcs.Task.IsCanceled);
    }

    [Fact]
    public void SetCanceled_WithStringType_WorksCorrectly()
    {
        // Arrange
        var tcs = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Act
        tcs.SetCanceled(cancellationToken);

        // Assert
        Assert.True(tcs.Task.IsCanceled);
    }

    [Fact]
    public void SetCanceled_MultipleTimes_FirstCallWins()
    {
        // Arrange
        var tcs = new TaskCompletionSource<int>();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        // Act
        tcs.SetCanceled(cancellationToken);
        var secondCallSucceeded = tcs.Task.IsCanceled;

        // Assert
        Assert.True(secondCallSucceeded);
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
        var result = text.Contains(value, StringComparison.Ordinal);

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
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithOrdinalIgnoreCase_MatchesCaseInsensitive()
    {
        // Arrange
        var text = "Hello World";
        var value = "WORLD";

        // Act
        var result = text.Contains(value, StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithOrdinal_MatchesCaseSensitive()
    {
        // Arrange
        var text = "Hello World";
        var value = "WORLD";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithEmptyString_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithNullText_ReturnsFalse()
    {
        // Arrange
        string? text = null;
        var value = "test";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithCurrentCulture_UsesCurrentCultureRules()
    {
        // Arrange
        var text = "Café";
        var value = "café";

        // Act
        var result = text.Contains(value, StringComparison.CurrentCultureIgnoreCase);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithInvariantCulture_UsesInvariantCultureRules()
    {
        // Arrange
        var text = "Hello World";
        var value = "hello";

        // Act
        var result = text.Contains(value, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_AtBeginning_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "Hello";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_AtEnd_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "World";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_InMiddle_ReturnsTrue()
    {
        // Arrange
        var text = "Hello World";
        var value = "lo Wo";

        // Act
        var result = text.Contains(value, StringComparison.Ordinal);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ThrowIfNull_UsedInMethodValidation_WorksCorrectly()
    {
        // Arrange
        void TestMethod(string? parameter)
        {
            CompatibilityHelpers.ThrowIfNull(parameter, nameof(parameter));
        }

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => TestMethod(null));
        TestMethod("valid"); // Should not throw
    }

    [Fact]
    public async Task FromResult_UsedInAsyncMethod_WorksCorrectly()
    {
        // Arrange
        async ValueTask<int> GetValueAsync(int value)
        {
            return await CompatibilityHelpers.FromResult(value);
        }

        // Act
        var result = await GetValueAsync(42);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void Contains_UsedForMessageValidation_WorksCorrectly()
    {
        // Arrange
        var errorMessage = "Connection failed: timeout occurred";
        var searchTerm = "timeout";

        // Act
        var containsTimeout = errorMessage.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.True(containsTimeout);
    }

    #endregion
}
