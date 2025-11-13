using HeroMessaging.Abstractions.Idempotency;

namespace HeroMessaging.Abstractions.Tests.Idempotency;

[Trait("Category", "Unit")]
public class IdempotencyStatusTests
{
    [Fact]
    public void Success_HasValueZero()
    {
        // Arrange & Act
        var status = IdempotencyStatus.Success;

        // Assert
        Assert.Equal(0, (int)status);
    }

    [Fact]
    public void Failure_HasValueOne()
    {
        // Arrange & Act
        var status = IdempotencyStatus.Failure;

        // Assert
        Assert.Equal(1, (int)status);
    }

    [Fact]
    public void Processing_HasValueTwo()
    {
        // Arrange & Act
        var status = IdempotencyStatus.Processing;

        // Assert
        Assert.Equal(2, (int)status);
    }

    [Fact]
    public void AllValues_AreDefined()
    {
        // Arrange
        var expectedValues = new[] { 0, 1, 2 };

        // Act
        var actualValues = Enum.GetValues<IdempotencyStatus>()
            .Select(v => (int)v)
            .ToArray();

        // Assert
        Assert.Equal(3, actualValues.Length);
        Assert.All(expectedValues, expected => Assert.Contains(expected, actualValues));
    }

    [Fact]
    public void ToString_ReturnsEnumName()
    {
        // Arrange & Act
        var successString = IdempotencyStatus.Success.ToString();
        var failureString = IdempotencyStatus.Failure.ToString();
        var processingString = IdempotencyStatus.Processing.ToString();

        // Assert
        Assert.Equal("Success", successString);
        Assert.Equal("Failure", failureString);
        Assert.Equal("Processing", processingString);
    }

    [Fact]
    public void Equality_WorksCorrectly()
    {
        // Arrange
        var status1 = IdempotencyStatus.Success;
        var status2 = IdempotencyStatus.Success;
        var status3 = IdempotencyStatus.Failure;

        // Act & Assert
        Assert.Equal(status1, status2);
        Assert.NotEqual(status1, status3);
        Assert.True(status1 == status2);
        Assert.True(status1 != status3);
    }

    [Fact]
    public void CanBeUsedInSwitchExpression()
    {
        // Arrange & Act
        var successMessage = GetStatusMessage(IdempotencyStatus.Success);
        var failureMessage = GetStatusMessage(IdempotencyStatus.Failure);
        var processingMessage = GetStatusMessage(IdempotencyStatus.Processing);

        // Assert
        Assert.Equal("Operation completed successfully", successMessage);
        Assert.Equal("Operation failed", failureMessage);
        Assert.Equal("Operation in progress", processingMessage);

        static string GetStatusMessage(IdempotencyStatus status) => status switch
        {
            IdempotencyStatus.Success => "Operation completed successfully",
            IdempotencyStatus.Failure => "Operation failed",
            IdempotencyStatus.Processing => "Operation in progress",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    [Fact]
    public void CanBeUsedInDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<IdempotencyStatus, string>
        {
            { IdempotencyStatus.Success, "Success handler" },
            { IdempotencyStatus.Failure, "Failure handler" },
            { IdempotencyStatus.Processing, "Processing handler" }
        };

        // Act & Assert
        Assert.Equal("Success handler", dictionary[IdempotencyStatus.Success]);
        Assert.Equal("Failure handler", dictionary[IdempotencyStatus.Failure]);
        Assert.Equal("Processing handler", dictionary[IdempotencyStatus.Processing]);
    }

    [Fact]
    public void CanBeCastToInt()
    {
        // Arrange
        var status = IdempotencyStatus.Success;

        // Act
        var intValue = (int)status;

        // Assert
        Assert.Equal(0, intValue);
    }

    [Fact]
    public void CanBeCastFromInt()
    {
        // Arrange
        var intValue = 1;

        // Act
        var status = (IdempotencyStatus)intValue;

        // Assert
        Assert.Equal(IdempotencyStatus.Failure, status);
    }

    [Fact]
    public void EnumParse_WorksCorrectly()
    {
        // Arrange & Act
        var success = Enum.Parse<IdempotencyStatus>("Success");
        var failure = Enum.Parse<IdempotencyStatus>("Failure");
        var processing = Enum.Parse<IdempotencyStatus>("Processing");

        // Assert
        Assert.Equal(IdempotencyStatus.Success, success);
        Assert.Equal(IdempotencyStatus.Failure, failure);
        Assert.Equal(IdempotencyStatus.Processing, processing);
    }

    [Fact]
    public void EnumParse_IgnoreCase_WorksCorrectly()
    {
        // Arrange & Act
        var success = Enum.Parse<IdempotencyStatus>("success", ignoreCase: true);
        var failure = Enum.Parse<IdempotencyStatus>("FAILURE", ignoreCase: true);

        // Assert
        Assert.Equal(IdempotencyStatus.Success, success);
        Assert.Equal(IdempotencyStatus.Failure, failure);
    }

    [Fact]
    public void TryParse_WithValidValue_ReturnsTrue()
    {
        // Arrange & Act
        var result = Enum.TryParse<IdempotencyStatus>("Success", out var status);

        // Assert
        Assert.True(result);
        Assert.Equal(IdempotencyStatus.Success, status);
    }

    [Fact]
    public void TryParse_WithInvalidValue_ReturnsFalse()
    {
        // Arrange & Act
        var result = Enum.TryParse<IdempotencyStatus>("Invalid", out var status);

        // Assert
        Assert.False(result);
        Assert.Equal(default, status);
    }

    [Fact]
    public void GetValues_ReturnsAllEnumValues()
    {
        // Arrange & Act
        var values = Enum.GetValues<IdempotencyStatus>();

        // Assert
        Assert.Equal(3, values.Length);
        Assert.Contains(IdempotencyStatus.Success, values);
        Assert.Contains(IdempotencyStatus.Failure, values);
        Assert.Contains(IdempotencyStatus.Processing, values);
    }

    [Fact]
    public void GetNames_ReturnsAllEnumNames()
    {
        // Arrange & Act
        var names = Enum.GetNames<IdempotencyStatus>();

        // Assert
        Assert.Equal(3, names.Length);
        Assert.Contains("Success", names);
        Assert.Contains("Failure", names);
        Assert.Contains("Processing", names);
    }

    [Fact]
    public void CompareTo_WorksCorrectly()
    {
        // Arrange
        var success = IdempotencyStatus.Success;
        var failure = IdempotencyStatus.Failure;
        var processing = IdempotencyStatus.Processing;

        // Act & Assert
        Assert.True(success.CompareTo(failure) < 0);
        Assert.True(failure.CompareTo(processing) < 0);
        Assert.True(success.CompareTo(success) == 0);
    }
}
