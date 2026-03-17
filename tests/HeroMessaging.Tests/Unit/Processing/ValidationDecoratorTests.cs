using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing
{
    [Trait("Category", "Unit")]
    public sealed class ValidationDecoratorTests
    {
        private readonly Mock<IMessageProcessor> _innerMock;
        private readonly Mock<IMessageValidator> _validatorMock;
        private readonly Mock<ILogger<ValidationDecorator>> _loggerMock;

        public ValidationDecoratorTests()
        {
            _innerMock = new Mock<IMessageProcessor>();
            _validatorMock = new Mock<IMessageValidator>();
            _loggerMock = new Mock<ILogger<ValidationDecorator>>();
        }

        private ValidationDecorator CreateDecorator()
        {
            return new ValidationDecorator(_innerMock.Object, _validatorMock.Object, _loggerMock.Object);
        }

        #region ProcessAsync - Valid Message

        [Fact]
        public async Task ProcessAsync_WithValidMessage_CallsInnerProcessor()
        {
            // Arrange
            var decorator = CreateDecorator();
            var message = new TestMessage();
            var context = new ProcessingContext();

            _validatorMock
                .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Success());

            _innerMock
                .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProcessingResult.Successful());

            // Act
            var result = await decorator.ProcessAsync(message, context);

            // Assert
            Assert.True(result.Success);
            _innerMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region ProcessAsync - Invalid Message

        [Fact]
        public async Task ProcessAsync_WithInvalidMessage_ReturnsFailureWithoutCallingInner()
        {
            // Arrange
            var decorator = CreateDecorator();
            var message = new TestMessage();
            var context = new ProcessingContext();
            var validationErrors = new[] { "Field1 is required", "Field2 must be positive" };

            _validatorMock
                .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Failure(validationErrors));

            // Act
            var result = await decorator.ProcessAsync(message, context);

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.Exception);
            Assert.IsType<ValidationException>(result.Exception);
            Assert.Contains("Field1 is required", result.Message);
            Assert.Contains("Field2 must be positive", result.Message);
            _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_WithInvalidMessage_LogsWarning()
        {
            // Arrange
            var decorator = CreateDecorator();
            var message = new TestMessage();
            var context = new ProcessingContext();

            _validatorMock
                .Setup(v => v.ValidateAsync(message, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Failure("Validation failed"));

            // Act
            await decorator.ProcessAsync(message, context);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region ProcessAsync - Cancellation

        [Fact]
        public async Task ProcessAsync_PassesCancellationTokenToValidator()
        {
            // Arrange
            var decorator = CreateDecorator();
            var message = new TestMessage();
            var context = new ProcessingContext();
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            _validatorMock
                .Setup(v => v.ValidateAsync(message, cancellationToken))
                .ReturnsAsync(ValidationResult.Success());

            _innerMock
                .Setup(p => p.ProcessAsync(message, context, cancellationToken))
                .ReturnsAsync(ProcessingResult.Successful());

            // Act
            await decorator.ProcessAsync(message, context, cancellationToken);

            // Assert
            _validatorMock.Verify(v => v.ValidateAsync(message, cancellationToken), Times.Once);
            _innerMock.Verify(p => p.ProcessAsync(message, context, cancellationToken), Times.Once);
        }

        #endregion

        #region Test Helper Classes

        public class TestMessage : IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        #endregion
    }

    [Trait("Category", "Unit")]
    public sealed class CompositeValidatorTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNoValidators_CreatesInstance()
        {
            // Act
            var validator = new CompositeValidator();

            // Assert
            Assert.NotNull(validator);
        }

        [Fact]
        public void Constructor_WithMultipleValidators_CreatesInstance()
        {
            // Arrange
            var validator1 = new Mock<IMessageValidator>().Object;
            var validator2 = new Mock<IMessageValidator>().Object;

            // Act
            var validator = new CompositeValidator(validator1, validator2);

            // Assert
            Assert.NotNull(validator);
        }

        #endregion

        #region ValidateAsync Tests

        [Fact]
        public async Task ValidateAsync_WithNoValidators_ReturnsSuccess()
        {
            // Arrange
            var validator = new CompositeValidator();
            var message = new TestMessage();

            // Act
            var result = await validator.ValidateAsync(message);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public async Task ValidateAsync_WithAllValidValidators_ReturnsSuccess()
        {
            // Arrange
            var validator1 = new Mock<IMessageValidator>();
            validator1.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Success());

            var validator2 = new Mock<IMessageValidator>();
            validator2.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Success());

            var validator = new CompositeValidator(validator1.Object, validator2.Object);
            var message = new TestMessage();

            // Act
            var result = await validator.ValidateAsync(message);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task ValidateAsync_WithOneInvalidValidator_ReturnsFailure()
        {
            // Arrange
            var validator1 = new Mock<IMessageValidator>();
            validator1.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Success());

            var validator2 = new Mock<IMessageValidator>();
            validator2.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Failure("Validation failed"));

            var validator = new CompositeValidator(validator1.Object, validator2.Object);
            var message = new TestMessage();

            // Act
            var result = await validator.ValidateAsync(message);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Validation failed", result.Errors);
        }

        [Fact]
        public async Task ValidateAsync_WithMultipleInvalidValidators_AggregatesErrors()
        {
            // Arrange
            var validator1 = new Mock<IMessageValidator>();
            validator1.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Failure("Error 1", "Error 2"));

            var validator2 = new Mock<IMessageValidator>();
            validator2.Setup(v => v.ValidateAsync(It.IsAny<IMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationResult.Failure("Error 3"));

            var validator = new CompositeValidator(validator1.Object, validator2.Object);
            var message = new TestMessage();

            // Act
            var result = await validator.ValidateAsync(message);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(3, result.Errors.Length);
            Assert.Contains("Error 1", result.Errors);
            Assert.Contains("Error 2", result.Errors);
            Assert.Contains("Error 3", result.Errors);
        }

        #endregion

        #region Test Helper Classes

        public class TestMessage : IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        #endregion
    }
}
