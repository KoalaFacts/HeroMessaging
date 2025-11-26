using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using HeroMessaging.Validation;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Validation;

[Trait("Category", "Unit")]
public sealed class MessageSizeValidatorTests
{
    private readonly Mock<IJsonSerializer> _jsonSerializerMock;

    public MessageSizeValidatorTests()
    {
        _jsonSerializerMock = new Mock<IJsonSerializer>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithMaxSizeAndSerializer_CreatesInstance()
    {
        // Arrange & Act
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithSerializerOnly_UsesDefaultMaxSize()
    {
        // Arrange & Act
        var validator = new MessageSizeValidator(_jsonSerializerMock.Object);

        // Assert
        Assert.NotNull(validator);
    }

    [Fact]
    public void Constructor_WithNullSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MessageSizeValidator(1024, null!));

        Assert.Equal("jsonSerializer", exception.ParamName);
    }

    #endregion

    #region ValidateAsync - Valid Message

    [Fact]
    public async Task ValidateAsync_WithMessageUnderLimit_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(500);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithMessageAtExactLimit_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1024);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WithVerySmallMessage_ReturnsSuccess()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage(content: "a");

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region ValidateAsync - Invalid Message (Size Exceeded)

    [Fact]
    public async Task ValidateAsync_WithMessageOverLimit_ReturnsFailure()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(2048);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("2048 bytes exceeds maximum allowed size of 1024 bytes", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithMessageOneByteOverLimit_ReturnsFailure()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1025);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("1025 bytes exceeds maximum allowed size of 1024 bytes", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithMessageExceedsLimit_ErrorMessageContainsActualAndMaxSize()
    {
        // Arrange
        var validator = new MessageSizeValidator(500, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1000);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("1000 bytes", result.Errors[0]);
        Assert.Contains("500 bytes", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Edge Cases

    [Fact]
    public async Task ValidateAsync_WithZeroMaxSize_ReturnsFailureForAnyMessage()
    {
        // Arrange
        var validator = new MessageSizeValidator(0, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum allowed size of 0 bytes", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WithLargeMaxSize_AllowsLargeMessages()
    {
        // Arrange
        var validator = new MessageSizeValidator(10 * 1024 * 1024, _jsonSerializerMock.Object); // 10MB
        var message = TestMessageBuilder.CreateLargeMessage(1000000);

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(5 * 1024 * 1024); // 5MB

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithNullMessage_CallsSerializer()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        IMessage? message = null;

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(10);

        // Act
        var result = await validator.ValidateAsync(message!);

        // Assert
        _jsonSerializerMock.Verify(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()), Times.Once);
    }

    #endregion

    #region ValidateAsync - Serialization Errors

    [Fact]
    public async Task ValidateAsync_WhenSerializerThrowsException_ReturnsFailureWithErrorMessage()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Throws(new InvalidOperationException("Serialization error"));

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Failed to validate message size", result.Errors[0]);
        Assert.Contains("Serialization error", result.Errors[0]);
    }

    [Fact]
    public async Task ValidateAsync_WhenSerializerThrowsArgumentException_ReturnsFailure()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Throws(new ArgumentException("Invalid argument"));

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Failed to validate message size", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Cancellation Token

    [Fact]
    public async Task ValidateAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();
        var cts = new CancellationTokenSource();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(500);

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithCancelledToken_DoesNotThrow()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(500);

        // Act
        var result = await validator.ValidateAsync(message, cts.Token);

        // Assert - No exception, validation completes
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAsync - Configuration Options

    [Fact]
    public async Task ValidateAsync_WithCustomMaxSize_UsesSpecifiedLimit()
    {
        // Arrange
        var customMaxSize = 2048;
        var validator = new MessageSizeValidator(customMaxSize, _jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1500);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDefaultMaxSize_UsesOneMillionBytes()
    {
        // Arrange
        var validator = new MessageSizeValidator(_jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        // Default is 1MB (1024 * 1024 = 1048576 bytes)
        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1048576);

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithDefaultMaxSize_FailsWhenExceedingOneMillionBytes()
    {
        // Arrange
        var validator = new MessageSizeValidator(_jsonSerializerMock.Object);
        var message = TestMessageBuilder.CreateValidMessage();

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(1048577); // 1 byte over 1MB

        // Act
        var result = await validator.ValidateAsync(message);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("1048577 bytes exceeds maximum allowed size of 1048576 bytes", result.Errors[0]);
    }

    #endregion

    #region ValidateAsync - Multiple Calls

    [Fact]
    public async Task ValidateAsync_CalledMultipleTimes_CallsSerializerEachTime()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var message1 = TestMessageBuilder.CreateValidMessage("Message 1");
        var message2 = TestMessageBuilder.CreateValidMessage("Message 2");

        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(500);

        // Act
        await validator.ValidateAsync(message1);
        await validator.ValidateAsync(message2);

        // Assert
        _jsonSerializerMock.Verify(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentMessages_ReturnsCorrectResultsIndependently()
    {
        // Arrange
        var validator = new MessageSizeValidator(1024, _jsonSerializerMock.Object);
        var smallMessage = TestMessageBuilder.CreateValidMessage("small");
        var largeMessage = TestMessageBuilder.CreateLargeMessage();

        // Setup different return values for different calls using callback
        var callCount = 0;
        _jsonSerializerMock
            .Setup(s => s.GetJsonByteCount<IMessage>(It.IsAny<IMessage>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(() => callCount++ == 0 ? 100 : 2000);

        // Act
        var result1 = await validator.ValidateAsync(smallMessage);
        var result2 = await validator.ValidateAsync(largeMessage);

        // Assert
        Assert.True(result1.IsValid);
        Assert.False(result2.IsValid);
    }

    #endregion
}
