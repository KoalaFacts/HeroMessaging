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

    #region Exception Handling Tests

    [Fact]
    public async Task ProcessAsync_WhenValidatorThrowsException_PropagatesException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedException = new InvalidOperationException("Validator error");

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _innerProcessorMock.Verify(p => p.ProcessAsync(
            It.IsAny<IMessage>(),
            It.IsAny<ProcessingContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenInnerProcessorThrowsException_PropagatesException()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var expectedException = new InvalidOperationException("Processor error");

        _validatorMock.Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        _innerProcessorMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        var decorator = new ValidationDecorator(
            _innerProcessorMock.Object,
            _validatorMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ProcessAsync(message, context));

        _validatorMock.Verify(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Processing Result Variation Tests

    [Fact]
    public async Task ProcessAsync_WithValidMessageAndProcessingFailure_ReturnsInnerFailure()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
        var context = new ProcessingContext();
        var processingException = new InvalidOperationException("Processing failed");
        var expectedResult = ProcessingResult.Failed(
            processingException,
            "Processing failure message");

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
        Assert.False(result.Success);
        Assert.Same(processingException, result.Exception);
        Assert.Equal("Processing failure message", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithValidMessageAndProcessingSuccess_ReturnsInnerSuccess()
    {
        // Arrange
        var message = new TestMessage { Content = "test" };
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
        Assert.Null(result.Exception);
    }

    #endregion

    #region Logging Verification Tests

    [Fact]
    public async Task ProcessAsync_WithInvalidMessage_LogsWarningWithMessageIdAndErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var errors = new[] { "Error 1", "Error 2" };
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
        // Verify logging was called (MockLogger captures Log calls through LogWarning extension)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithValidMessage_DoesNotLog()
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
        // Verify logging was NOT called for successful validation
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region Error Message Format Tests

    [Fact]
    public async Task ProcessAsync_WithInvalidMessage_FailureMessageContainsAllErrors()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var errors = new[] { "First error", "Second error", "Third error" };
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
        Assert.NotNull(result.Message);
        Assert.Contains("First error", result.Message);
        Assert.Contains("Second error", result.Message);
        Assert.Contains("Third error", result.Message);
        Assert.Contains("Validation failed:", result.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithEmptyValidationErrors_StillReturnsFailureAsInvalid()
    {
        // Arrange
        var message = new TestMessage { Content = "invalid" };
        var context = new ProcessingContext();
        var validationResult = ValidationResult.Failure(Array.Empty<string>());

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
    }

    #endregion

    #region Test Message Class

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    #endregion
}
