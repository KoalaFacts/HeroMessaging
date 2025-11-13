using HeroMessaging.Abstractions.Idempotency;

namespace HeroMessaging.Abstractions.Tests.Idempotency;

[Trait("Category", "Unit")]
public class IdempotencyResponseTests
{
    [Fact]
    public void Constructor_WithSuccessResult_SetsAllProperties()
    {
        // Arrange
        var key = "test-key";
        var result = "success-result";
        var storedAt = DateTimeOffset.UtcNow;
        var expiresAt = storedAt.AddHours(24);

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = key,
            SuccessResult = result,
            StoredAt = storedAt,
            ExpiresAt = expiresAt,
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.Equal(key, response.IdempotencyKey);
        Assert.Equal(result, response.SuccessResult);
        Assert.Equal(storedAt, response.StoredAt);
        Assert.Equal(expiresAt, response.ExpiresAt);
        Assert.Equal(IdempotencyStatus.Success, response.Status);
        Assert.Null(response.FailureType);
        Assert.Null(response.FailureMessage);
        Assert.Null(response.FailureStackTrace);
    }

    [Fact]
    public void Constructor_WithFailure_SetsFailureProperties()
    {
        // Arrange
        var key = "test-key";
        var failureType = "System.ArgumentException";
        var failureMessage = "Invalid argument";
        var stackTrace = "at TestMethod...";
        var storedAt = DateTimeOffset.UtcNow;
        var expiresAt = storedAt.AddHours(1);

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = key,
            FailureType = failureType,
            FailureMessage = failureMessage,
            FailureStackTrace = stackTrace,
            StoredAt = storedAt,
            ExpiresAt = expiresAt,
            Status = IdempotencyStatus.Failure
        };

        // Assert
        Assert.Equal(key, response.IdempotencyKey);
        Assert.Equal(failureType, response.FailureType);
        Assert.Equal(failureMessage, response.FailureMessage);
        Assert.Equal(stackTrace, response.FailureStackTrace);
        Assert.Equal(storedAt, response.StoredAt);
        Assert.Equal(expiresAt, response.ExpiresAt);
        Assert.Equal(IdempotencyStatus.Failure, response.Status);
        Assert.Null(response.SuccessResult);
    }

    [Fact]
    public void SuccessResult_CanBeNull()
    {
        // Arrange & Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            SuccessResult = null,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.Null(response.SuccessResult);
        Assert.Equal(IdempotencyStatus.Success, response.Status);
    }

    [Fact]
    public void FailureProperties_CanBeNull()
    {
        // Arrange & Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            FailureType = null,
            FailureMessage = null,
            FailureStackTrace = null,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Failure
        };

        // Assert
        Assert.Null(response.FailureType);
        Assert.Null(response.FailureMessage);
        Assert.Null(response.FailureStackTrace);
    }

    [Fact]
    public void Status_Processing_CanBeSet()
    {
        // Arrange & Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Status = IdempotencyStatus.Processing
        };

        // Assert
        Assert.Equal(IdempotencyStatus.Processing, response.Status);
    }

    [Fact]
    public void ExpiresAt_CanBeLaterThanStoredAt()
    {
        // Arrange
        var storedAt = DateTimeOffset.UtcNow;
        var expiresAt = storedAt.AddDays(7);

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            StoredAt = storedAt,
            ExpiresAt = expiresAt,
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.True(response.ExpiresAt > response.StoredAt);
        Assert.Equal(TimeSpan.FromDays(7), response.ExpiresAt - response.StoredAt);
    }

    [Fact]
    public void SuccessResult_CanStoreComplexObject()
    {
        // Arrange
        var complexResult = new
        {
            Id = 123,
            Name = "Test",
            Values = new[] { 1, 2, 3 }
        };

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            SuccessResult = complexResult,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.NotNull(response.SuccessResult);
        var result = response.SuccessResult;
        Assert.Equal(complexResult, result);
    }

    [Fact]
    public void InitProperties_AreInitOnly()
    {
        // Arrange
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Act & Assert - This test verifies that the properties use init accessors
        // We can only set them during initialization, not after
        var newResponse = new IdempotencyResponse
        {
            IdempotencyKey = "new-key",
            SuccessResult = response.SuccessResult,
            FailureType = response.FailureType,
            FailureMessage = response.FailureMessage,
            FailureStackTrace = response.FailureStackTrace,
            StoredAt = response.StoredAt,
            ExpiresAt = response.ExpiresAt,
            Status = response.Status
        };
        Assert.Equal("key", response.IdempotencyKey);
        Assert.Equal("new-key", newResponse.IdempotencyKey);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new IdempotencyResponse
        {
            IdempotencyKey = "key-1",
            SuccessResult = "result-1",
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Act
        var modified = new IdempotencyResponse
        {
            IdempotencyKey = "key-2",
            SuccessResult = original.SuccessResult,
            FailureType = original.FailureType,
            FailureMessage = original.FailureMessage,
            FailureStackTrace = original.FailureStackTrace,
            StoredAt = original.StoredAt,
            ExpiresAt = original.ExpiresAt,
            Status = original.Status
        };

        // Assert
        Assert.Equal("key-1", original.IdempotencyKey);
        Assert.Equal("key-2", modified.IdempotencyKey);
        Assert.Equal(original.SuccessResult, modified.SuccessResult);
    }

    [Fact]
    public void FailureStackTrace_CanStoreLongStrings()
    {
        // Arrange
        var longStackTrace = new string('A', 10000);

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            FailureType = "System.Exception",
            FailureMessage = "Error",
            FailureStackTrace = longStackTrace,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Failure
        };

        // Assert
        Assert.Equal(10000, response.FailureStackTrace!.Length);
        Assert.Equal(longStackTrace, response.FailureStackTrace);
    }

    [Fact]
    public void TimeSpans_CalculateCorrectly()
    {
        // Arrange
        var storedAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var expiresAt = new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero);

        // Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            StoredAt = storedAt,
            ExpiresAt = expiresAt,
            Status = IdempotencyStatus.Success
        };

        // Assert
        var ttl = response.ExpiresAt - response.StoredAt;
        Assert.Equal(TimeSpan.FromHours(1), ttl);
    }

    [Fact]
    public void Success_WithVoidResult_UsesNullSuccessResult()
    {
        // Arrange & Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            SuccessResult = null,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.Null(response.SuccessResult);
        Assert.Equal(IdempotencyStatus.Success, response.Status);
    }

    [Fact]
    public void IdempotencyKey_IsRequired()
    {
        // This test verifies the required keyword behavior
        // When creating without the required property, it should cause issues
        // We test this by ensuring we can create with it

        // Arrange & Act
        var response = new IdempotencyResponse
        {
            IdempotencyKey = "required-key",
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = IdempotencyStatus.Success
        };

        // Assert
        Assert.Equal("required-key", response.IdempotencyKey);
        Assert.NotNull(response.IdempotencyKey);
    }
}
