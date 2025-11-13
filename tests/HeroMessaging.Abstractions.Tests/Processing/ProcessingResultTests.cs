using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Tests.Processing;

[Trait("Category", "Unit")]
public class ProcessingResultTests
{
    [Fact]
    public void Successful_WithNoParameters_CreatesSuccessResult()
    {
        // Arrange & Act
        var result = ProcessingResult.Successful();

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Exception);
        Assert.Null(result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Successful_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var result = ProcessingResult.Successful("Operation completed");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Operation completed", result.Message);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Successful_WithData_SetsData()
    {
        // Arrange
        var data = new { Id = 123, Value = "test" };

        // Act
        var result = ProcessingResult.Successful(data: data);

        // Assert
        Assert.True(result.Success);
        Assert.Same(data, result.Data);
    }

    [Fact]
    public void Successful_WithMessageAndData_SetsBoth()
    {
        // Arrange
        var data = "test-data";

        // Act
        var result = ProcessingResult.Successful("Success message", data);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Success message", result.Message);
        Assert.Equal(data, result.Data);
    }

    [Fact]
    public void Failed_WithException_SetsException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = ProcessingResult.Failed(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Same(exception, result.Exception);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Failed_WithExceptionAndMessage_SetsBoth()
    {
        // Arrange
        var exception = new InvalidOperationException("Inner error");

        // Act
        var result = ProcessingResult.Failed(exception, "Failed to process");

        // Assert
        Assert.False(result.Success);
        Assert.Same(exception, result.Exception);
        Assert.Equal("Failed to process", result.Message);
    }

    [Fact]
    public void DefaultStruct_IsFailure()
    {
        // Arrange & Act
        var result = default(ProcessingResult);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Exception);
        Assert.Null(result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Equality_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var exception = new Exception("Test");
        var result1 = ProcessingResult.Failed(exception, "Error");
        var result2 = ProcessingResult.Failed(exception, "Error");

        // Act & Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Inequality_WithDifferentSuccess_ReturnsTrue()
    {
        // Arrange
        var result1 = ProcessingResult.Successful();
        var result2 = ProcessingResult.Failed(new Exception());

        // Act & Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void StructBehavior_IsValueType()
    {
        // Arrange
        var result1 = ProcessingResult.Successful("Test");

        // Act
        var result2 = result1;

        // Assert
        Assert.Equal(result1.Success, result2.Success);
        Assert.Equal(result1.Message, result2.Message);
    }

    [Fact]
    public void CanBeUsedInCollections()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Successful("Done")
        };

        // Act
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        // Assert
        Assert.Equal(2, successCount);
        Assert.Equal(1, failureCount);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = ProcessingResult.Successful("Original");

        // Act
        var modified = original with { Message = "Modified" };

        // Assert
        Assert.Equal("Original", original.Message);
        Assert.Equal("Modified", modified.Message);
        Assert.True(modified.Success);
    }

    [Fact]
    public void ComplexData_CanBeStored()
    {
        // Arrange
        var complexData = new
        {
            Results = new[] { 1, 2, 3 },
            Metadata = new Dictionary<string, object> { { "key", "value" } },
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = ProcessingResult.Successful(data: complexData);

        // Assert
        Assert.Same(complexData, result.Data);
    }

    [Fact]
    public void ExceptionType_IsPreserved()
    {
        // Arrange
        var specificException = new ArgumentNullException("param");

        // Act
        var result = ProcessingResult.Failed(specificException);

        // Assert
        Assert.IsType<ArgumentNullException>(result.Exception);
        Assert.Equal("param", ((ArgumentNullException)result.Exception!).ParamName);
    }

    [Fact]
    public void MultipleResults_CanBeDifferentiated()
    {
        // Arrange & Act
        var success1 = ProcessingResult.Successful("Success 1");
        var success2 = ProcessingResult.Successful("Success 2");
        var failure1 = ProcessingResult.Failed(new Exception("Error 1"));
        var failure2 = ProcessingResult.Failed(new Exception("Error 2"));

        // Assert
        Assert.True(success1.Success);
        Assert.True(success2.Success);
        Assert.False(failure1.Success);
        Assert.False(failure2.Success);
        Assert.NotEqual(success1.Message, success2.Message);
        Assert.NotEqual(failure1.Exception!.Message, failure2.Exception!.Message);
    }

    [Fact]
    public void GetHashCode_WithEqualValues_ReturnsSameHash()
    {
        // Arrange
        var result1 = ProcessingResult.Successful("Test");
        var result2 = ProcessingResult.Successful("Test");

        // Act
        var hash1 = result1.GetHashCode();
        var hash2 = result2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ToString_ReturnsReadableString()
    {
        // Arrange
        var result = ProcessingResult.Successful("Test");

        // Act
        var str = result.ToString();

        // Assert
        Assert.NotNull(str);
        Assert.NotEmpty(str);
    }
}
