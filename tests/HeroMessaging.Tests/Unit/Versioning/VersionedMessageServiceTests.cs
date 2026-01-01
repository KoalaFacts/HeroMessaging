using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Versioning;

/// <summary>
/// Unit tests for VersionedMessageService class
/// </summary>
public class VersionedMessageServiceTests
{
    private readonly Mock<IMessageVersionResolver> _mockVersionResolver;
    private readonly Mock<IMessageConverterRegistry> _mockConverterRegistry;
    private readonly Mock<ILogger<VersionedMessageService>> _mockLogger;
    private readonly VersionedMessageService _service;

    public VersionedMessageServiceTests()
    {
        _mockVersionResolver = new Mock<IMessageVersionResolver>();
        _mockConverterRegistry = new Mock<IMessageConverterRegistry>();
        _mockLogger = new Mock<ILogger<VersionedMessageService>>();
        _service = new VersionedMessageService(
            _mockVersionResolver.Object,
            _mockConverterRegistry.Object,
            _mockLogger.Object);
    }

    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
        public string Data { get; set; } = string.Empty;
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullVersionResolver_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new VersionedMessageService(null!, _mockConverterRegistry.Object, _mockLogger.Object));
        Assert.Equal("versionResolver", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConverterRegistry_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new VersionedMessageService(_mockVersionResolver.Object, null!, _mockLogger.Object));
        Assert.Equal("converterRegistry", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Act
        var service = new VersionedMessageService(
            _mockVersionResolver.Object,
            _mockConverterRegistry.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region ConvertToVersionAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var targetVersion = new MessageVersion(2, 0);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _service.ConvertToVersionAsync<TestMessage>(null!, targetVersion));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WhenAlreadyAtTargetVersion_ReturnsOriginalMessage()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var version = new MessageVersion(1, 0);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(version);

        // Act
        var result = await _service.ConvertToVersionAsync(message, version, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(message, result);
        _mockConverterRegistry.Verify(x => x.FindConversionPath(It.IsAny<Type>(), It.IsAny<MessageVersion>(), It.IsAny<MessageVersion>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WhenNoConversionPathExists_ThrowsMessageConversionException()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(fromVersion);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns((MessageConversionPath?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MessageConversionException>(async () =>
            await _service.ConvertToVersionAsync(message, toVersion, TestContext.Current.CancellationToken));
        Assert.Contains("No conversion path found", exception.Message);
        Assert.Contains("TestMessage", exception.Message);
        Assert.Contains("1.0", exception.Message);
        Assert.Contains("2.0", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WithSingleStepConversion_ReturnsConvertedMessage()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);
        var convertedMessage = new TestMessage { Data = "converted" };

        var mockConverter = new Mock<IMessageConverter>();
        mockConverter.Setup(x => x.ConvertAsync(message, fromVersion, toVersion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(convertedMessage);

        var conversionPath = new MessageConversionPath(
            typeof(TestMessage),
            fromVersion,
            toVersion,
            [
                new MessageConversionStep(fromVersion, toVersion, mockConverter.Object)
            ]);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(fromVersion);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns(conversionPath);

        // Act
        var result = await _service.ConvertToVersionAsync(message, toVersion, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(convertedMessage, result);
        mockConverter.Verify(x => x.ConvertAsync(message, fromVersion, toVersion, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WithMultiStepConversion_AppliesAllSteps()
    {
        // Arrange
        var message = new TestMessage { Data = "v1" };
        var v1 = new MessageVersion(1, 0);
        var v2 = new MessageVersion(2, 0);
        var v3 = new MessageVersion(3, 0);

        var intermediateMessage = new TestMessage { Data = "v2" };
        var finalMessage = new TestMessage { Data = "v3" };

        var converter1 = new Mock<IMessageConverter>();
        converter1.Setup(x => x.ConvertAsync(message, v1, v2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(intermediateMessage);

        var converter2 = new Mock<IMessageConverter>();
        converter2.Setup(x => x.ConvertAsync(intermediateMessage, v2, v3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalMessage);

        var conversionPath = new MessageConversionPath(
            typeof(TestMessage),
            v1,
            v3,
            [
                new MessageConversionStep(v1, v2, converter1.Object),
                new MessageConversionStep(v2, v3, converter2.Object)
            ]);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(v1);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), v1, v3))
            .Returns(conversionPath);

        // Act
        var result = await _service.ConvertToVersionAsync(message, v3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(finalMessage, result);
        converter1.Verify(x => x.ConvertAsync(message, v1, v2, It.IsAny<CancellationToken>()), Times.Once);
        converter2.Verify(x => x.ConvertAsync(intermediateMessage, v2, v3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WhenConversionStepFails_ThrowsMessageConversionExceptionWithInnerException()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);
        var innerException = new InvalidOperationException("Conversion logic failed");

        var mockConverter = new Mock<IMessageConverter>();
        mockConverter.Setup(x => x.ConvertAsync(message, fromVersion, toVersion, It.IsAny<CancellationToken>()))
            .ThrowsAsync(innerException);

        var conversionPath = new MessageConversionPath(
            typeof(TestMessage),
            fromVersion,
            toVersion,
            [
                new MessageConversionStep(fromVersion, toVersion, mockConverter.Object)
            ]);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(fromVersion);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns(conversionPath);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<MessageConversionException>(async () =>
            await _service.ConvertToVersionAsync(message, toVersion, TestContext.Current.CancellationToken));
        Assert.Contains("Conversion failed at step", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConvertToVersionAsync_WithCancellationToken_PassesTokenToConverter()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);
        var cts = new CancellationTokenSource();
        var convertedMessage = new TestMessage { Data = "converted" };

        var mockConverter = new Mock<IMessageConverter>();
        mockConverter.Setup(x => x.ConvertAsync(message, fromVersion, toVersion, cts.Token))
            .ReturnsAsync(convertedMessage);

        var conversionPath = new MessageConversionPath(
            typeof(TestMessage),
            fromVersion,
            toVersion,
            [
                new MessageConversionStep(fromVersion, toVersion, mockConverter.Object)
            ]);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(fromVersion);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns(conversionPath);

        // Act
        var result = await _service.ConvertToVersionAsync(message, toVersion, cts.Token, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(convertedMessage, result);
        mockConverter.Verify(x => x.ConvertAsync(message, fromVersion, toVersion, cts.Token), Times.Once);
    }

    #endregion

    #region EnsureCompatibilityAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EnsureCompatibilityAsync_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var requiredVersion = new MessageVersion(2, 0);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _service.EnsureCompatibilityAsync<TestMessage>(null!, requiredVersion));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EnsureCompatibilityAsync_WhenVersionIsCompatible_ReturnsOriginalMessage()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var currentVersion = new MessageVersion(2, 1);
        var requiredVersion = new MessageVersion(2, 0);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(currentVersion);

        // Act
        var result = await _service.EnsureCompatibilityAsync(message, requiredVersion, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(message, result);
        _mockConverterRegistry.Verify(x => x.FindConversionPath(It.IsAny<Type>(), It.IsAny<MessageVersion>(), It.IsAny<MessageVersion>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EnsureCompatibilityAsync_WhenVersionIsNotCompatible_PerformsConversion()
    {
        // Arrange
        var message = new TestMessage { Data = "v1" };
        var currentVersion = new MessageVersion(1, 0);
        var requiredVersion = new MessageVersion(2, 0);
        var convertedMessage = new TestMessage { Data = "v2" };

        var mockConverter = new Mock<IMessageConverter>();
        mockConverter.Setup(x => x.ConvertAsync(message, currentVersion, requiredVersion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(convertedMessage);

        var conversionPath = new MessageConversionPath(
            typeof(TestMessage),
            currentVersion,
            requiredVersion,
            [
                new MessageConversionStep(currentVersion, requiredVersion, mockConverter.Object)
            ]);

        _mockVersionResolver.Setup(x => x.GetVersion(message))
            .Returns(currentVersion);
        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), currentVersion, requiredVersion))
            .Returns(conversionPath);

        // Act
        var result = await _service.EnsureCompatibilityAsync(message, requiredVersion, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(convertedMessage, result);
        mockConverter.Verify(x => x.ConvertAsync(message, currentVersion, requiredVersion, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ValidateMessage Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidateMessage_WithNullMessage_ThrowsArgumentNullException()
    {
        // Arrange
        var targetVersion = new MessageVersion(2, 0);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.ValidateMessage<TestMessage>(null!, targetVersion));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidateMessage_DelegatesToVersionResolver()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var targetVersion = new MessageVersion(2, 0);
        var expectedResult = new MessageVersionValidationResult(true, [], []);

        _mockVersionResolver.Setup(x => x.ValidateMessage(message, targetVersion))
            .Returns(expectedResult);

        // Act
        var result = _service.ValidateMessage(message, targetVersion);

        // Assert
        Assert.Same(expectedResult, result);
        _mockVersionResolver.Verify(x => x.ValidateMessage(message, targetVersion), Times.Once);
    }

    #endregion

    #region GetVersionInfo Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void GetVersionInfo_WithGenericType_DelegatesToVersionResolver()
    {
        // Arrange
        var expectedInfo = new MessageVersionInfo(
            typeof(TestMessage),
            new MessageVersion(1, 0),
            "TestMessage",
            []);

        _mockVersionResolver.Setup(x => x.GetVersionInfo(typeof(TestMessage)))
            .Returns(expectedInfo);

        // Act
        var result = _service.GetVersionInfo<TestMessage>();

        // Assert
        Assert.Same(expectedInfo, result);
        _mockVersionResolver.Verify(x => x.GetVersionInfo(typeof(TestMessage)), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetVersionInfo_WithMessageInstance_WithNullMessage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.GetVersionInfo<TestMessage>(null!));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetVersionInfo_WithMessageInstance_DelegatesToVersionResolver()
    {
        // Arrange
        var message = new TestMessage { Data = "test" };
        var expectedInfo = new MessageVersionInfo(
            typeof(TestMessage),
            new MessageVersion(1, 0),
            "TestMessage",
            []);

        _mockVersionResolver.Setup(x => x.GetVersionInfo(typeof(TestMessage)))
            .Returns(expectedInfo);

        // Act
        var result = _service.GetVersionInfo(message);

        // Assert
        Assert.Same(expectedInfo, result);
        _mockVersionResolver.Verify(x => x.GetVersionInfo(typeof(TestMessage)), Times.Once);
    }

    #endregion

    #region CanConvert Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void CanConvert_DelegatesToConverterRegistry()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);

        _mockConverterRegistry.Setup(x => x.CanConvert(typeof(TestMessage), fromVersion, toVersion))
            .Returns(true);

        // Act
        var result = _service.CanConvert<TestMessage>(fromVersion, toVersion);

        // Assert
        Assert.True(result);
        _mockConverterRegistry.Verify(x => x.CanConvert(typeof(TestMessage), fromVersion, toVersion), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CanConvert_WhenNoConversionExists_ReturnsFalse()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(3, 0);

        _mockConverterRegistry.Setup(x => x.CanConvert(typeof(TestMessage), fromVersion, toVersion))
            .Returns(false);

        // Act
        var result = _service.CanConvert<TestMessage>(fromVersion, toVersion);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region FindConversionPath Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void FindConversionPath_DelegatesToConverterRegistry()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(2, 0);
        var mockConverter = new Mock<IMessageConverter>();
        var expectedPath = new MessageConversionPath(
            typeof(TestMessage),
            fromVersion,
            toVersion,
            [
                new MessageConversionStep(fromVersion, toVersion, mockConverter.Object)
            ]);

        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns(expectedPath);

        // Act
        var result = _service.FindConversionPath<TestMessage>(fromVersion, toVersion);

        // Assert
        Assert.Same(expectedPath, result);
        _mockConverterRegistry.Verify(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion), Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FindConversionPath_WhenNoPathExists_ReturnsNull()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0);
        var toVersion = new MessageVersion(3, 0);

        _mockConverterRegistry.Setup(x => x.FindConversionPath(typeof(TestMessage), fromVersion, toVersion))
            .Returns((MessageConversionPath?)null);

        // Act
        var result = _service.FindConversionPath<TestMessage>(fromVersion, toVersion);

        // Assert
        Assert.Null(result);
    }

    #endregion
}
