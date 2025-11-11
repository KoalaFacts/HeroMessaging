using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

/// <summary>
/// Unit tests for IdempotencyDecorator
/// Tests the idempotency decorator in the message processing pipeline
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdempotencyDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<IIdempotencyStore> _storeMock;
    private readonly Mock<IIdempotencyPolicy> _policyMock;
    private readonly Mock<IIdempotencyKeyGenerator> _keyGeneratorMock;
    private readonly Mock<ILogger<IdempotencyDecorator>> _loggerMock;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IdempotencyDecorator _decorator;

    public IdempotencyDecoratorTests()
    {
        _innerProcessorMock = new Mock<IMessageProcessor>();
        _storeMock = new Mock<IIdempotencyStore>();
        _policyMock = new Mock<IIdempotencyPolicy>();
        _keyGeneratorMock = new Mock<IIdempotencyKeyGenerator>();
        _loggerMock = new Mock<ILogger<IdempotencyDecorator>>();
        _timeProvider = new FakeTimeProvider();

        _policyMock.Setup(p => p.KeyGenerator).Returns(_keyGeneratorMock.Object);
        _policyMock.Setup(p => p.SuccessTtl).Returns(TimeSpan.FromHours(1));
        _policyMock.Setup(p => p.FailureTtl).Returns(TimeSpan.FromMinutes(10));
        _policyMock.Setup(p => p.CacheFailures).Returns(true);

        _decorator = new IdempotencyDecorator(
            _innerProcessorMock.Object,
            _storeMock.Object,
            _policyMock.Object,
            _loggerMock.Object,
            _timeProvider);
    }

    #region Cache Hit Tests

    [Fact]
    public async Task ProcessAsync_CacheHit_ReturnsFromCacheWithoutInvokingInner()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";
        var cachedData = new { Result = "cached" };

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            SuccessResult = cachedData,
            Status = IdempotencyStatus.Success,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(cachedData, result.Data);
        _innerProcessorMock.Verify(
            p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_CachedFailure_ReturnsFailureWithoutInvokingInner()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            FailureType = typeof(InvalidOperationException).FullName,
            FailureMessage = "Cached error",
            Status = IdempotencyStatus.Failure,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Contains("Cached error", result.Exception.Message);
        _innerProcessorMock.Verify(
            p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Cache Miss - Success Tests

    [Fact]
    public async Task ProcessAsync_CacheMiss_InvokesInnerAndStoresSuccess()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";
        var resultData = new { Result = "success" };

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful(data: resultData));

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(resultData, result.Data);

        _storeMock.Verify(
            s => s.StoreSuccessAsync(idempotencyKey, resultData, TimeSpan.FromHours(1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SuccessWithNullData_StoresSuccessfully()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.True(result.Success);

        _storeMock.Verify(
            s => s.StoreSuccessAsync(idempotencyKey, null, TimeSpan.FromHours(1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Cache Miss - Failure Tests

    [Fact]
    public async Task ProcessAsync_IdempotentFailure_StoresFailure()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";
        var exception = new InvalidOperationException("Business rule violation");

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));

        _policyMock
            .Setup(p => p.IsIdempotentFailure(exception))
            .Returns(true);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);

        _storeMock.Verify(
            s => s.StoreFailureAsync(idempotencyKey, exception, TimeSpan.FromMinutes(10), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NonIdempotentFailure_DoesNotStoreFailure()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";
        var exception = new TimeoutException("Transient error");

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));

        _policyMock
            .Setup(p => p.IsIdempotentFailure(exception))
            .Returns(false);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);

        _storeMock.Verify(
            s => s.StoreFailureAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_CacheFailuresDisabled_DoesNotStoreFailure()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";
        var exception = new InvalidOperationException("Error");

        _policyMock.Setup(p => p.CacheFailures).Returns(false);

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);

        _storeMock.Verify(
            s => s.StoreFailureAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Key Generation Tests

    [Fact]
    public async Task ProcessAsync_CallsKeyGeneratorWithCorrectArguments()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns("generated-key");

        _storeMock
            .Setup(s => s.GetAsync("generated-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await _decorator.ProcessAsync(message, context);

        // Assert
        _keyGeneratorMock.Verify(
            g => g.GenerateKey(message, context),
            Times.Once);
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public async Task Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(
                null!,
                _storeMock.Object,
                _policyMock.Object,
                _loggerMock.Object,
                _timeProvider));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(
                _innerProcessorMock.Object,
                null!,
                _policyMock.Object,
                _loggerMock.Object,
                _timeProvider));
        Assert.Equal("store", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(
                _innerProcessorMock.Object,
                _storeMock.Object,
                null!,
                _loggerMock.Object,
                _timeProvider));
        Assert.Equal("policy", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(
                _innerProcessorMock.Object,
                _storeMock.Object,
                _policyMock.Object,
                null!,
                _timeProvider));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(
                _innerProcessorMock.Object,
                _storeMock.Object,
                _policyMock.Object,
                _loggerMock.Object,
                null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region Exception Reconstruction Tests

    [Fact]
    public async Task ProcessAsync_CachedFailureWithoutStackTrace_ReconstructsException()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            FailureType = typeof(ArgumentException).FullName,
            FailureMessage = "Invalid argument",
            FailureStackTrace = null,
            Status = IdempotencyStatus.Failure,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentException>(result.Exception);
        Assert.Equal("Invalid argument", result.Exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_CachedFailureWithUnknownType_ReturnsGenericException()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component");
        var idempotencyKey = "test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            FailureType = "NonExistent.Exception.Type",
            FailureMessage = "Unknown error",
            Status = IdempotencyStatus.Failure,
            StoredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<Exception>(result.Exception);
        Assert.Contains("Unknown error", result.Exception.Message);
    }

    #endregion

    #region Integration with ProcessingContext Tests

    [Fact]
    public async Task ProcessAsync_PreservesProcessingContext()
    {
        // Arrange
        var message = TestMessageBuilder.CreateValidMessage("test");
        var context = new ProcessingContext("test-component")
            .WithMetadata("custom-key", "custom-value");
        var idempotencyKey = "test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(idempotencyKey);

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        ProcessingContext? capturedContext = null;
        _innerProcessorMock
            .Setup(p => p.ProcessAsync(message, It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .Callback<IMessage, ProcessingContext, CancellationToken>((m, c, ct) => capturedContext = c)
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal("custom-value", capturedContext.Value.GetMetadataReference<string>("custom-key"));
    }

    #endregion
}
