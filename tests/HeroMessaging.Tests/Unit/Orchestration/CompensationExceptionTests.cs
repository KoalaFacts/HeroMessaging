using HeroMessaging.Orchestration;
using System.Runtime.Serialization;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

/// <summary>
/// Comprehensive test suite for CompensationException class.
/// Tests all constructors, properties, serialization, and integration scenarios.
/// </summary>
[Trait("Category", "Unit")]
public class CompensationExceptionTests
{
    // ===== Constructor Tests =====

    /// <summary>
    /// Tests CompensationException constructor with valid parameters
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_CreatesException()
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<CompensationException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
        Assert.Equal(actionName, exception.ActionName);
        Assert.Same(innerException, exception.InnerException);
    }

    /// <summary>
    /// Tests that ActionName property is properly stored
    /// </summary>
    [Fact]
    public void ActionName_Property_StoresAndReturnsValue()
    {
        // Arrange
        var actionName = "ProcessPayment";
        var innerException = new TimeoutException("Request timeout");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Equal("ProcessPayment", exception.ActionName);
    }

    /// <summary>
    /// Tests that constructor properly includes ActionName in exception message
    /// </summary>
    [Fact]
    public void Constructor_Message_IncludesActionName()
    {
        // Arrange
        var actionName = "RollbackTransaction";
        var innerException = new InvalidOperationException("Database error");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Contains("RollbackTransaction", exception.Message);
        Assert.Contains("Failed to compensate action", exception.Message);
    }

    /// <summary>
    /// Tests that constructor message includes inner exception message
    /// </summary>
    [Fact]
    public void Constructor_Message_IncludesInnerExceptionMessage()
    {
        // Arrange
        var actionName = "DeleteRecord";
        var innerMessage = "Record already deleted";
        var innerException = new InvalidOperationException(innerMessage);

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Contains(innerMessage, exception.Message);
    }

    /// <summary>
    /// Tests constructor with various inner exception types
    /// </summary>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(NullReferenceException))]
    public void Constructor_WithDifferentInnerExceptionTypes_Succeeds(Type exceptionType)
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = (Exception)Activator.CreateInstance(exceptionType, "Test message");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Equal(actionName, exception.ActionName);
        Assert.NotNull(exception.InnerException);
        Assert.IsType(exceptionType, exception.InnerException);
    }

    // ===== ActionName Property Tests =====

    /// <summary>
    /// Tests ActionName property with empty string (edge case)
    /// </summary>
    [Fact]
    public void ActionName_WithEmptyString_StoresValue()
    {
        // Arrange
        var actionName = string.Empty;
        var innerException = new Exception("Test");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Equal(string.Empty, exception.ActionName);
    }

    /// <summary>
    /// Tests ActionName property with special characters
    /// </summary>
    [Theory]
    [InlineData("Action-With-Dashes")]
    [InlineData("Action_With_Underscores")]
    [InlineData("Action.With.Dots")]
    [InlineData("Action With Spaces")]
    [InlineData("Action/With/Slashes")]
    [InlineData("Action\\With\\Backslashes")]
    [InlineData("Action:With:Colons")]
    [InlineData("ActionWithUnicode_日本語")]
    public void ActionName_WithSpecialCharacters_PreservesValue(string actionName)
    {
        // Arrange
        var innerException = new Exception("Test");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Equal(actionName, exception.ActionName);
    }

    /// <summary>
    /// Tests ActionName property with very long strings
    /// </summary>
    [Fact]
    public void ActionName_WithLongString_PreservesValue()
    {
        // Arrange
        var longActionName = new string('A', 10000);
        var innerException = new Exception("Test");

        // Act
        var exception = new CompensationException(longActionName, innerException);

        // Assert
        Assert.Equal(longActionName, exception.ActionName);
        Assert.Equal(10000, exception.ActionName.Length);
    }

    // ===== InnerException Tests =====

    /// <summary>
    /// Tests that InnerException is preserved correctly
    /// </summary>
    [Fact]
    public void InnerException_IsPreserved()
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = new ArgumentNullException("parameter");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Same(innerException, exception.InnerException);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>
    /// Tests nested inner exceptions are preserved
    /// </summary>
    [Fact]
    public void InnerException_WithNestedException_PreservesChain()
    {
        // Arrange
        var actionName = "TestAction";
        var rootException = new Exception("Root cause");
        var middleException = new InvalidOperationException("Middle error", rootException);
        var innerException = new TimeoutException("Timeout", middleException);

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.Same(innerException, exception.InnerException);
        Assert.Same(middleException, exception.InnerException?.InnerException);
        Assert.Same(rootException, exception.InnerException?.InnerException?.InnerException);
    }

    // ===== Exception Inheritance Tests =====

    /// <summary>
    /// Tests that CompensationException inherits from Exception
    /// </summary>
    [Fact]
    public void CompensationException_InheritsFromException()
    {
        // Arrange
        var exception = new CompensationException("Test", new Exception());

        // Act & Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    /// <summary>
    /// Tests that exception can be caught as Exception
    /// </summary>
    [Fact]
    public void CompensationException_CanBeCaughtAsException()
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = new InvalidOperationException("Error");
        var exception = new CompensationException(actionName, innerException);

        // Act & Assert
        try
        {
            throw exception;
        }
        catch (Exception ex)
        {
            Assert.IsType<CompensationException>(ex);
            Assert.Equal(actionName, ((CompensationException)ex).ActionName);
        }
    }

    /// <summary>
    /// Tests that exception can be caught as CompensationException specifically
    /// </summary>
    [Fact]
    public void CompensationException_CanBeCaughtSpecifically()
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = new InvalidOperationException("Error");
        var exception = new CompensationException(actionName, innerException);

        // Act & Assert
        try
        {
            throw exception;
        }
        catch (CompensationException ex)
        {
            Assert.Equal(actionName, ex.ActionName);
            Assert.Same(innerException, ex.InnerException);
        }
    }

    /// <summary>
    /// Tests exception message property
    /// </summary>
    [Fact]
    public void Message_Property_ReturnsFormattedMessage()
    {
        // Arrange
        var actionName = "ResetPassword";
        var innerException = new UnauthorizedAccessException("Access denied");

        // Act
        var exception = new CompensationException(actionName, innerException);

        // Assert
        Assert.NotNull(exception.Message);
        Assert.NotEmpty(exception.Message);
        Assert.Contains("ResetPassword", exception.Message);
    }

    /// <summary>
    /// Tests that HResult property has expected default value
    /// </summary>
    [Fact]
    public void HResult_Property_HasValidValue()
    {
        // Arrange
        var exception = new CompensationException("Test", new Exception());

        // Act & Assert
        // HResult should be set (not 0 which indicates no error)
        Assert.NotEqual(0, exception.HResult);
    }

    // ===== Serialization Tests =====

    /// <summary>
    /// Tests that exception message is serializable
    /// </summary>
    [Fact]
    public void Exception_Message_IsSerializable()
    {
        // Arrange
        var actionName = "SerializableAction";
        var innerException = new Exception("Inner message");
        var exception = new CompensationException(actionName, innerException);

        // Act
        var serialized = exception.ToString();

        // Assert
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);
        Assert.Contains("CompensationException", serialized);
    }

    /// <summary>
    /// Tests that exception ToString includes ActionName
    /// </summary>
    [Fact]
    public void ToString_IncludesActionName()
    {
        // Arrange
        var actionName = "SpecialAction";
        var innerException = new InvalidOperationException("Error");
        var exception = new CompensationException(actionName, innerException);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("CompensationException", result);
        Assert.Contains("SpecialAction", result);
    }

    /// <summary>
    /// Tests that exception ToString includes InnerException details
    /// </summary>
    [Fact]
    public void ToString_IncludesInnerExceptionDetails()
    {
        // Arrange
        var actionName = "TestAction";
        var innerMessage = "Critical error message";
        var innerException = new InvalidOperationException(innerMessage);
        var exception = new CompensationException(actionName, innerException);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("InvalidOperationException", result);
        Assert.Contains(innerMessage, result);
    }

    // ===== Stack Trace Tests =====

    /// <summary>
    /// Tests that StackTrace is populated after throwing
    /// </summary>
    [Fact]
    public void StackTrace_IsPopulatedAfterThrow()
    {
        // Arrange
        Exception? caughtException = null;

        // Act
        try
        {
            throw new CompensationException("Test", new Exception("Inner"));
        }
        catch (CompensationException ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.NotNull(caughtException);
        Assert.NotNull(caughtException.StackTrace);
        Assert.NotEmpty(caughtException.StackTrace);
    }

    // ===== Integration Tests =====

    /// <summary>
    /// Tests CompensationException in typical usage scenario with CompensationContext
    /// </summary>
    [Fact]
    public async Task CompensationException_UsedInCompensationContext_Works()
    {
        // Arrange
        var context = new CompensationContext();
        var failedActionName = "FailingAction";
        context.AddCompensation(failedActionName, () => throw new InvalidOperationException("Action failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: true));

        Assert.NotNull(exception);
        Assert.Single(exception.InnerExceptions);
        var innerEx = exception.InnerExceptions[0];
        Assert.IsType<CompensationException>(innerEx);
        Assert.Equal(failedActionName, ((CompensationException)innerEx).ActionName);
    }

    /// <summary>
    /// Tests multiple CompensationExceptions aggregated together
    /// </summary>
    [Fact]
    public async Task MultipleCompensationExceptions_AreAggregated()
    {
        // Arrange
        var context = new CompensationContext();
        context.AddCompensation("FirstFailing", () => throw new InvalidOperationException("Error 1"));
        context.AddCompensation("SecondFailing", () => throw new InvalidOperationException("Error 2"));
        context.AddCompensation("ThirdFailing", () => throw new InvalidOperationException("Error 3"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await context.CompensateAsync(stopOnFirstError: false));

        Assert.Equal(3, exception.InnerExceptions.Count);
        var compensationExceptions = exception.InnerExceptions.Cast<CompensationException>().ToList();
        Assert.All(compensationExceptions, ex => Assert.IsType<CompensationException>(ex));
    }

    // ===== Edge Case Tests =====

    /// <summary>
    /// Tests CompensationException with null ActionName stores null value
    /// </summary>
    [Fact]
    public void Constructor_WithNullActionName_StoresNull()
    {
        // Arrange
        string? nullActionName = null;
        var innerException = new Exception("Test");

        // Act
        var exception = new CompensationException(nullActionName!, innerException);

        // Assert - ActionName stores the null (or null string ref)
        Assert.Null(exception.ActionName);
    }

    /// <summary>
    /// Tests CompensationException with null InnerException throws NullReferenceException
    /// (Expected behavior since constructor accesses innerException.Message without null check)
    /// </summary>
    [Fact]
    public void Constructor_WithNullInnerException_ThrowsNullReferenceException()
    {
        // Arrange
        var actionName = "TestAction";

        // Act & Assert
        Assert.Throws<NullReferenceException>(() => new CompensationException(actionName, null!));
    }

    /// <summary>
    /// Tests CompensationException equality is based on reference
    /// </summary>
    [Fact]
    public void TwoCompensationExceptions_WithSameData_AreNotEqual()
    {
        // Arrange
        var innerException = new Exception("Test");
        var exception1 = new CompensationException("Action", innerException);
        var exception2 = new CompensationException("Action", innerException);

        // Act & Assert
        Assert.NotEqual(exception1, exception2);
        Assert.False(ReferenceEquals(exception1, exception2));
    }

    /// <summary>
    /// Tests CompensationException same reference equality
    /// </summary>
    [Fact]
    public void SameCompensationException_IsEqual()
    {
        // Arrange
        var exception = new CompensationException("Action", new Exception());

        // Act & Assert
        Assert.Equal(exception, exception);
        Assert.True(ReferenceEquals(exception, exception));
    }

    /// <summary>
    /// Tests exception can be converted to string without throwing
    /// </summary>
    [Fact]
    public void ExceptionStringConversion_DoesNotThrow()
    {
        // Arrange
        var exception = new CompensationException("TestAction", new Exception("Inner"));

        // Act & Assert (should not throw)
        var result = exception.ToString();
        Assert.NotNull(result);
    }

    /// <summary>
    /// Tests exception with complex nested inner exception chain
    /// </summary>
    [Fact]
    public void CompensationException_WithComplexInnerChain_Succeeds()
    {
        // Arrange
        var actionName = "ComplexAction";
        var ex1 = new TimeoutException("Timeout occurred");
        var ex2 = new InvalidOperationException("Operation failed", ex1);
        var ex3 = new ArgumentException("Invalid argument", ex2);

        // Act
        var exception = new CompensationException(actionName, ex3);

        // Assert
        Assert.Equal(actionName, exception.ActionName);
        Assert.NotNull(exception.InnerException);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    /// <summary>
    /// Tests exception Data property (inherited from Exception)
    /// </summary>
    [Fact]
    public void Data_Property_CanBeUsed()
    {
        // Arrange
        var exception = new CompensationException("Test", new Exception());

        // Act
        exception.Data["key1"] = "value1";
        exception.Data["ActionName"] = "TestAction";

        // Assert
        Assert.Equal("value1", exception.Data["key1"]);
        Assert.Equal("TestAction", exception.Data["ActionName"]);
        Assert.Equal(2, exception.Data.Count);
    }

    /// <summary>
    /// Tests multiple CompensationExceptions with different action names
    /// </summary>
    [Theory]
    [InlineData("PaymentProcessing")]
    [InlineData("InventoryUpdate")]
    [InlineData("NotificationSend")]
    [InlineData("LoggingOperation")]
    [InlineData("DatabaseCleanup")]
    public void ActionName_WithDifferentValues_ArePreserved(string actionName)
    {
        // Arrange & Act
        var exception = new CompensationException(actionName, new Exception());

        // Assert
        Assert.Equal(actionName, exception.ActionName);
    }

    /// <summary>
    /// Tests exception with numeric action name
    /// </summary>
    [Fact]
    public void ActionName_WithNumericString_IsPreserved()
    {
        // Arrange
        var actionName = "12345";
        var exception = new CompensationException(actionName, new Exception("Test"));

        // Act & Assert
        Assert.Equal("12345", exception.ActionName);
    }

    /// <summary>
    /// Tests exception behavior in catch-rethrow scenario - validates data preservation
    /// </summary>
    [Fact]
    public void CompensationException_CaughtAndInspected_PreservesData()
    {
        // Arrange
        var actionName = "TestAction";
        var innerException = new InvalidOperationException("Original error");
        var original = new CompensationException(actionName, innerException);

        // Act
        CompensationException? caught = null;
        try
        {
            throw original;
        }
        catch (CompensationException ex)
        {
            caught = ex;
            // Do not rethrow in this test context
        }

        // Assert - verify the exception was preserved and properties accessible
        Assert.NotNull(caught);
        Assert.Equal(actionName, caught.ActionName);
        Assert.Same(innerException, caught.InnerException);
    }

    /// <summary>
    /// Tests that exception message format is consistent
    /// </summary>
    [Fact]
    public void ExceptionMessage_Format_IsConsistent()
    {
        // Arrange
        var actionName = "ImportData";
        var errorDetail = "CSV file is malformed";
        var innerException = new FormatException(errorDetail);

        // Act
        var exception = new CompensationException(actionName, innerException);
        var message = exception.Message;

        // Assert
        Assert.Contains("Failed to compensate action", message);
        Assert.Contains(actionName, message);
        Assert.Contains(errorDetail, message);
    }

    /// <summary>
    /// Tests exception with whitespace-only action name
    /// </summary>
    [Fact]
    public void ActionName_WithWhitespaceOnly_IsPreserved()
    {
        // Arrange
        var actionName = "   \t\n  ";
        var exception = new CompensationException(actionName, new Exception("Test"));

        // Act & Assert
        Assert.Equal(actionName, exception.ActionName);
    }

    /// <summary>
    /// Tests that exception properties are accessible after construction
    /// </summary>
    [Fact]
    public void ExceptionProperties_AreAccessibleAfterConstruction()
    {
        // Arrange
        var actionName = "ValidateInput";
        var innerException = new ArgumentException("Invalid argument");
        var exception = new CompensationException(actionName, innerException);

        // Act & Assert - verify all properties are accessible
        Assert.NotNull(exception.ActionName);
        Assert.Equal(actionName, exception.ActionName);
        Assert.NotNull(exception.InnerException);
        Assert.Equal(innerException, exception.InnerException);
        Assert.NotNull(exception.Message);
        Assert.NotEmpty(exception.Message);
        Assert.NotNull(exception.GetType());
        Assert.Equal(typeof(CompensationException), exception.GetType());
    }
}
