using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Validation;
using Xunit;
using RequiredAttribute = HeroMessaging.Validation.RequiredAttribute;

namespace HeroMessaging.Tests.Unit.Validation;

[Trait("Category", "Unit")]
public sealed class RequiredFieldsValidatorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var validator = new RequiredFieldsValidator();

        // Assert
        Assert.NotNull(validator);
    }

    #endregion

    #region ValidateAsync - Valid Messages

    [Fact]
    public async Task ValidateAsync_WithValidMessage_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = TestMessageBuilder.CreateValidMessage();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithValidMessageIdAndTimestamp_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithAllRequiredFieldsPopulated_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField = "value",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region ValidateAsync - Invalid MessageId

    [Fact]
    public async Task ValidateAsync_WithEmptyMessageId_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.Empty,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("MessageId is required and cannot be empty", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyMessageId_ErrorMessageIsDescriptive()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.Empty,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.Contains("MessageId", result.Errors[0]);
        Assert.Contains("required", result.Errors[0]);
        Assert.Contains("empty", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Invalid Timestamp

    [Fact]
    public async Task ValidateAsync_WithDefaultTimestamp_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = default,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Timestamp is required and cannot be default", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithMinValueTimestamp_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.MinValue,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Timestamp is required and cannot be default", result.Errors);
    }

    #endregion

    #region ValidateAsync - Multiple Validation Errors

    [Fact]
    public async Task ValidateAsync_WithEmptyMessageIdAndDefaultTimestamp_ReturnsMultipleErrors()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.Empty,
            Timestamp = default,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Length);
        Assert.Contains("MessageId is required and cannot be empty", result.Errors);
        Assert.Contains("Timestamp is required and cannot be default", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithAllInvalidFields_AggregatesAllErrors()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.Empty,
            Timestamp = default,
            RequiredField = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Length);
        Assert.Contains(result.Errors, e => e.Contains("MessageId"));
        Assert.Contains(result.Errors, e => e.Contains("Timestamp"));
        Assert.Contains(result.Errors, e => e.Contains("RequiredField"));
    }

    #endregion

    #region ValidateAsync - Required Attribute on Properties

    [Fact]
    public async Task ValidateAsync_WithNullRequiredProperty_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Property 'RequiredField' is required but was not provided", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyStringRequiredProperty_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField = string.Empty,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Property 'RequiredField' is required but was not provided", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithWhitespaceRequiredProperty_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField = "   ",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Property 'RequiredField' is required but was not provided", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithValidRequiredProperty_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField = "valid value",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithMultipleRequiredProperties_ValidatesAll()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithMultipleRequiredFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredField1 = null,
            RequiredField2 = string.Empty,
            RequiredField3 = "valid",
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Length);
        Assert.Contains(result.Errors, e => e.Contains("RequiredField1"));
        Assert.Contains(result.Errors, e => e.Contains("RequiredField2"));
    }

    #endregion

    #region ValidateAsync - Optional Properties

    [Fact]
    public async Task ValidateAsync_WithNullOptionalProperty_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithOptionalFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OptionalField = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotValidatePropertiesWithoutRequiredAttribute()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithOptionalFields
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            OptionalField = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Edge Cases

    [Fact]
    public async Task ValidateAsync_WithNullMetadata_DoesNotFailValidation()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = null
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithNullCorrelationId_DoesNotFailValidation()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithNullCausationId_DoesNotFailValidation()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CausationId = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithFutureTimestamp_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddDays(1),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithPastTimestamp_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddYears(-1),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Cancellation Token

    [Fact]
    public async Task ValidateAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = TestMessageBuilder.CreateValidMessage();
        var cts = new CancellationTokenSource();

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithCancelledToken_DoesNotThrow()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = TestMessageBuilder.CreateValidMessage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert - No exception, validation completes
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Multiple Calls

    [Fact]
    public async Task ValidateAsync_CalledMultipleTimes_ReturnsConsistentResults()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = TestMessageBuilder.CreateValidMessage();

        // Act
        var result1 = await validator.ValidateAsync(message);
        var result2 = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result1.IsValid);
        Assert.True(result2.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentMessages_ReturnsCorrectResultsIndependently()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var validMessage = TestMessageBuilder.CreateValidMessage();
        var invalidMessage = new TestMessage
        {
            MessageId = Guid.Empty,
            Timestamp = default,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result1 = await validator.ValidateAsync(validMessage);
        var result2 = await validator.ValidateAsync(invalidMessage);

        // Assert
        Assert.True(result1.IsValid);
        Assert.False(result2.IsValid);
    }

    #endregion

    #region ValidateAsync - Non-String Required Properties

    [Fact]
    public async Task ValidateAsync_WithNullNonStringRequiredProperty_ReturnsFailure()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithNonStringRequiredField
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredNumber = null,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Property 'RequiredNumber' is required but was not provided", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithZeroValueRequiredNumber_ReturnsSuccess()
    {
        // Arrange
        var validator = new RequiredFieldsValidator();
        var message = new MessageWithNonStringRequiredField
        {
            MessageId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RequiredNumber = 0,
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class MessageWithRequiredFields : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [Required]
        public string? RequiredField { get; set; }
    }

    public class MessageWithMultipleRequiredFields : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [Required]
        public string? RequiredField1 { get; set; }

        [Required]
        public string? RequiredField2 { get; set; }

        [Required]
        public string? RequiredField3 { get; set; }
    }

    public class MessageWithOptionalFields : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        public string? OptionalField { get; set; }
    }

    public class MessageWithNonStringRequiredField : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [Required]
        public int? RequiredNumber { get; set; }
    }

    #endregion
}

[Trait("Category", "Unit")]
public sealed class RequiredAttributeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var attribute = new RequiredAttribute();

        // Assert
        Assert.NotNull(attribute);
    }

    #endregion

    #region Attribute Validation

    [Fact]
    public void Attribute_IsAttributeClass()
    {
        // Arrange & Act
        var attribute = new RequiredAttribute();

        // Assert
        Assert.IsAssignableFrom<Attribute>(attribute);
    }

    [Fact]
    public void Attribute_CanBeAppliedToProperty()
    {
        // Arrange
        var type = typeof(TestMessageWithAttribute);
        var property = type.GetProperty(nameof(TestMessageWithAttribute.TestProperty));

        // Act
        var attribute = property?.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.IsType<RequiredAttribute>(attribute);
    }

    [Fact]
    public void Attribute_CannotBeAppliedMultipleTimes()
    {
        // Arrange
        var type = typeof(TestMessageWithAttribute);
        var property = type.GetProperty(nameof(TestMessageWithAttribute.TestProperty));

        // Act
        var attributes = property?.GetCustomAttributes(typeof(RequiredAttribute), false);

        // Assert
        Assert.NotNull(attributes);
        Assert.Single(attributes);
    }

    #endregion

    #region Test Helper Classes

    public class TestMessageWithAttribute : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [Required]
        public string? TestProperty { get; set; }
    }

    #endregion
}
