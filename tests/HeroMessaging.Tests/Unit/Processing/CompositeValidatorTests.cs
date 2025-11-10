using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing.Decorators;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for CompositeValidator
/// Tests combining multiple validators
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompositeValidatorTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithNoValidators_CreatesValidator()
    {
        // Act
        var validator = new CompositeValidator();

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithMultipleValidators_CreatesValidator()
    {
        // Arrange
        var validator1 = new Mock<IMessageValidator>().Object;
        var validator2 = new Mock<IMessageValidator>().Object;

        // Act
        var compositeValidator = new CompositeValidator(validator1, validator2);

        // Assert
        Assert.NotNull(compositeValidator);
    }

    #endregion

    #region ValidateAsync Success Tests

    [Fact]
    public async Task ValidateAsync_WithNoValidators_ReturnsSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var validator = new CompositeValidator();

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithAllValidatorsSucceeding_ReturnsSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };

        var validator1Mock = new Mock<IMessageValidator>();
        validator1Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        var validator2Mock = new Mock<IMessageValidator>();
        validator2Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        var compositeValidator = new CompositeValidator(validator1Mock.Object, validator2Mock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        validator1Mock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        validator2Mock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ValidateAsync Failure Tests

    [Fact]
    public async Task ValidateAsync_WithOneValidatorFailing_ReturnsFailureWithErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var errors = new[] { "Field is required" };

        var validator1Mock = new Mock<IMessageValidator>();
        validator1Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        var validator2Mock = new Mock<IMessageValidator>();
        validator2Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure(errors));

        var compositeValidator = new CompositeValidator(validator1Mock.Object, validator2Mock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Field is required", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithMultipleValidatorsFailing_AggregatesAllErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var errors1 = new[] { "Error from validator 1" };
        var errors2 = new[] { "Error from validator 2", "Another error from validator 2" };

        var validator1Mock = new Mock<IMessageValidator>();
        validator1Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure(errors1));

        var validator2Mock = new Mock<IMessageValidator>();
        validator2Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure(errors2));

        var compositeValidator = new CompositeValidator(validator1Mock.Object, validator2Mock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Length);
        Assert.Contains("Error from validator 1", result.Errors);
        Assert.Contains("Error from validator 2", result.Errors);
        Assert.Contains("Another error from validator 2", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithAllValidatorsFailing_CombinesAllErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };

        var validator1Mock = new Mock<IMessageValidator>();
        validator1Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure("Error 1"));

        var validator2Mock = new Mock<IMessageValidator>();
        validator2Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure("Error 2"));

        var validator3Mock = new Mock<IMessageValidator>();
        validator3Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure("Error 3"));

        var compositeValidator = new CompositeValidator(
            validator1Mock.Object,
            validator2Mock.Object,
            validator3Mock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Length);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
        Assert.Contains("Error 3", result.Errors);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ValidateAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var cts = new CancellationTokenSource();

        var validatorMock = new Mock<IMessageValidator>();
        validatorMock.Setup(v => v.ValidateAsync(message, cts.Token))
            .ReturnsAsync(ValidationResult.Success());

        var compositeValidator = new CompositeValidator(validatorMock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message, cts.Token);

        // Assert
        Assert.True(result.IsValid);
        validatorMock.Verify(v => v.ValidateAsync(message, cts.Token), Times.Once);
    }

    #endregion

    #region Validator Execution Order Tests

    [Fact]
    public async Task ValidateAsync_ExecutesAllValidatorsEvenIfOneFails()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };

        var validator1Mock = new Mock<IMessageValidator>();
        validator1Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure("Error 1"));

        var validator2Mock = new Mock<IMessageValidator>();
        validator2Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        var validator3Mock = new Mock<IMessageValidator>();
        validator3Mock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Failure("Error 3"));

        var compositeValidator = new CompositeValidator(
            validator1Mock.Object,
            validator2Mock.Object,
            validator3Mock.Object);

        // Act
        var result = await compositeValidator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        // All validators should have been called despite the first one failing
        validator1Mock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        validator2Mock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        validator3Mock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
