using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for ValidationDecorator
/// Tests message validation before processing
/// </summary>
[Trait("Category", "Unit")]
public sealed class ValidationDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<IMessageValidator> _validatorMock;
    private readonly Mock<ILogger<ValidationDecorator>> _loggerMock;

    public ValidationDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _validatorMock = new Mock<IMessageValidator>();
        _loggerMock = new Mock<ILogger<ValidationDecorator>>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesDecorator()
    {
        // Act
        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Validation Success Tests

    [Fact]
    public async Task ProcessAsync_WithValidMessage_ProcessesMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "valid" };
        var context = new ProcessingContext();
        var expectedResult = ProcessingResult.Successful();

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        _validatorMock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Validation Failure Tests

    [Fact]
    public async Task ProcessAsync_WithInvalidMessage_ReturnsFailureWithoutProcessing()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var errors = new[] { "Field is required", "Value is too long" };
        var validationResult = ValidationResult.Failure(errors);

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<ValidationException>(result.Exception);
        Assert.Contains("Field is required", result.Message);
        Assert.Contains("Value is too long", result.Message);
        _validatorMock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithSingleValidationError_ReturnsFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var errors = new[] { "Field is required" };
        var validationResult = ValidationResult.Failure(errors);

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.IsType<ValidationException>(result.Exception);
        var validationException = (ValidationException)result.Exception;
        Assert.Single(validationException.Errors);
        Assert.Contains("Field is required", validationException.Errors);
    }

    [Fact]
    public async Task ProcessAsync_WithMultipleValidationErrors_IncludesAllErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var errors = new[] { "Error 1", "Error 2", "Error 3" };
        var validationResult = ValidationResult.Failure(errors);

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        var validationException = (ValidationException)result.Exception!;
        Assert.Equal(3, validationException.Errors.Count);
        Assert.Contains("Error 1", validationException.Errors);
        Assert.Contains("Error 2", validationException.Errors);
        Assert.Contains("Error 3", validationException.Errors);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var expectedResult = ProcessingResult.Successful();

        _validatorMock.Setup(v => v.ValidateAsync(message, cts.Token))
            .ReturnsAsync(ValidationResult.Success());

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, cts.Token))
            .ReturnsAsync(expectedResult);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act
        var result = await decorator.ProcessAsync(message, context, cts.Token);

        // Assert
        Assert.True(result.Success);
        _validatorMock.Verify(v => v.ValidateAsync(message, cts.Token), Times.Once);
        _innerProcessorMock.Verify(p => p.ProcessAsync(message, context, cts.Token), Times.Once);
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
