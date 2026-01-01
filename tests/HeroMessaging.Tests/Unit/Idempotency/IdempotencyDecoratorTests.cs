using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

[Trait("Category", "Unit")]
public sealed class IdempotencyDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerMock;
    private readonly Mock<IIdempotencyStore> _storeMock;
    private readonly Mock<IIdempotencyPolicy> _policyMock;
    private readonly Mock<ILogger<IdempotencyDecorator>> _loggerMock;
    private readonly Mock<IIdempotencyKeyGenerator> _keyGeneratorMock;
    private readonly FakeTimeProvider _timeProvider;

    public IdempotencyDecoratorTests()
    {
        _innerMock = new Mock<IMessageProcessor>();
        _storeMock = new Mock<IIdempotencyStore>();
        _policyMock = new Mock<IIdempotencyPolicy>();
        _loggerMock = new Mock<ILogger<IdempotencyDecorator>>();
        _keyGeneratorMock = new Mock<IIdempotencyKeyGenerator>();
        _timeProvider = new FakeTimeProvider();

        // Default policy setup
        _policyMock.Setup(p => p.KeyGenerator).Returns(_keyGeneratorMock.Object);
        _policyMock.Setup(p => p.SuccessTtl).Returns(TimeSpan.FromHours(24));
        _policyMock.Setup(p => p.FailureTtl).Returns(TimeSpan.FromHours(1));
        _policyMock.Setup(p => p.CacheFailures).Returns(true);
    }

    private IdempotencyDecorator CreateDecorator()
    {
        return new IdempotencyDecorator(
            _innerMock.Object,
            _storeMock.Object,
            _policyMock.Object,
            _loggerMock.Object,
            _timeProvider);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Act
        var decorator = CreateDecorator();

        // Assert
        Assert.NotNull(decorator);
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(null!, _storeMock.Object, _policyMock.Object, _loggerMock.Object, _timeProvider));
        Assert.Equal("inner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(_innerMock.Object, null!, _policyMock.Object, _loggerMock.Object, _timeProvider));
        Assert.Equal("store", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(_innerMock.Object, _storeMock.Object, null!, _loggerMock.Object, _timeProvider));
        Assert.Equal("policy", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(_innerMock.Object, _storeMock.Object, _policyMock.Object, null!, _timeProvider));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IdempotencyDecorator(_innerMock.Object, _storeMock.Object, _policyMock.Object, _loggerMock.Object, null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region ProcessAsync - Cache Miss (New Request)

    [Fact]
    public async Task ProcessAsync_WithCacheMiss_GeneratesKey()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var expectedKey = "idempotency:test-key";

        _keyGeneratorMock
            .Setup(g => g.GenerateKey(message, context))
            .Returns(expectedKey);

        _storeMock
            .Setup(s => s.GetAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);

        _innerMock
            .Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _keyGeneratorMock.Verify(g => g.GenerateKey(message, context), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCacheMissAndSuccess_CallsInnerProcessor()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>())).ReturnsAsync(ProcessingResult.Successful());

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        _innerMock.Verify(p => p.ProcessAsync(message, context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCacheMissAndSuccess_StoresSuccessResult()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var resultData = new { Value = 42 };
        var idempotencyKey = "test-key";

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns(idempotencyKey);
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful(data: resultData));

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreSuccessAsync(
            idempotencyKey,
            resultData,
            _policyMock.Object.SuccessTtl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCacheMissAndSuccessNoData_StoresSuccessWithNullData()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var idempotencyKey = "test-key";

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns(idempotencyKey);
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreSuccessAsync(
            idempotencyKey,
            null,
            _policyMock.Object.SuccessTtl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ProcessAsync - Cache Miss with Idempotent Failure

    [Fact]
    public async Task ProcessAsync_WithIdempotentFailureAndCacheEnabled_StoresFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var exception = new ArgumentException("Invalid argument");
        var idempotencyKey = "test-key";

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns(idempotencyKey);
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));
        _policyMock.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        _storeMock.Verify(s => s.StoreFailureAsync(
            idempotencyKey,
            exception,
            _policyMock.Object.FailureTtl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithNonIdempotentFailure_DoesNotStoreFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var exception = new TimeoutException("Operation timed out");

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));
        _policyMock.Setup(p => p.IsIdempotentFailure(exception)).Returns(false);

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithIdempotentFailureButCacheDisabled_DoesNotStoreFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var exception = new ArgumentException("Invalid");

        _policyMock.Setup(p => p.CacheFailures).Returns(false);
        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));
        _policyMock.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithFailureNoException_DoesNotStoreFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessingResult { Success = false, Exception = null });

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ProcessAsync - Cache Hit (Duplicate Request)

    [Fact]
    public async Task ProcessAsync_WithCachedSuccess_ReturnsSuccessWithoutCallingInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedData = new { Value = 99 };
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Success,
            SuccessResult = cachedData,
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(cachedData, result.Data);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedSuccessNoData_ReturnsSuccessWithNullData()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Success,
            SuccessResult = null,
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_ReturnsFailureWithoutCallingInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Failure,
            FailureType = typeof(ArgumentException).FullName,
            FailureMessage = "Cached error",
            FailureStackTrace = "Stack trace here",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Contains("Cached error", result.Exception.Message);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_ReconstructsArgumentException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Failure,
            FailureType = typeof(ArgumentException).FullName,
            FailureMessage = "Invalid argument",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<ArgumentException>(result.Exception);
        Assert.Equal("Invalid argument", result.Exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_ReconstructsInvalidOperationException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Failure,
            FailureType = typeof(InvalidOperationException).FullName,
            FailureMessage = "Invalid operation",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_UnknownExceptionType_CreatesGenericException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Failure,
            FailureType = "NonExistent.CustomException",
            FailureMessage = "Custom error",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<Exception>(result.Exception);
        Assert.Contains("NonExistent.CustomException", result.Exception.Message);
        Assert.Contains("Custom error", result.Exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_NullFailureType_CreatesGenericException()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "key",
            Status = IdempotencyStatus.Failure,
            FailureType = null,
            FailureMessage = "Error message",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        var result = await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Contains("Error message", result.Exception.Message);
    }

    #endregion

    #region ProcessAsync - Logging Tests

    [Fact]
    public async Task ProcessAsync_WithCacheHit_LogsInformation()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = "test-key",
            Status = IdempotencyStatus.Success,
            SuccessResult = "data",
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
        };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("test-key");
        _storeMock.Setup(s => s.GetAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(cachedResponse);

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCacheMiss_LogsDebug()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessAsync_WithStoredIdempotentFailure_LogsWarning()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();
        var exception = new ArgumentException("Test");

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));
        _policyMock.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        await decorator.ProcessAsync(message, context, TestContext.Current.CancellationToken);

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

    #region ProcessAsync - Cancellation Tests

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToStore()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync("key", cancellationToken)).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(message, context, cancellationToken)).ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _storeMock.Verify(s => s.GetAsync("key", cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_PassesCancellationTokenToInner()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(message, context, cancellationToken)).ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _innerMock.Verify(p => p.ProcessAsync(message, context, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnSuccess_PassesCancellationTokenToStoreSuccess()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreSuccessAsync(
            It.IsAny<string>(),
            It.IsAny<object?>(),
            It.IsAny<TimeSpan>(),
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OnIdempotentFailure_PassesCancellationTokenToStoreFailure()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message = new TestMessage();
        var context = new ProcessingContext();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var exception = new ArgumentException("Test");

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns("key");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Failed(exception));
        _policyMock.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        await decorator.ProcessAsync(message, context, cancellationToken);

        // Assert
        _storeMock.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            cancellationToken), Times.Once);
    }

    #endregion

    #region Edge Cases and Integration Scenarios

    [Fact]
    public async Task ProcessAsync_MultipleCalls_SameMessage_ReturnsCachedResponseOnSecondCall()
    {
        // Arrange
        var decorator = CreateDecorator();
        var messageId = Guid.NewGuid();
        var message1 = new TestMessage { MessageId = messageId };
        var message2 = new TestMessage { MessageId = messageId };
        var context = new ProcessingContext();
        var idempotencyKey = $"idempotency:{messageId}";
        var resultData = new { Value = 123 };

        _keyGeneratorMock.Setup(g => g.GenerateKey(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>())).Returns(idempotencyKey);

        // First call - cache miss
        _storeMock.SetupSequence(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null)
            .ReturnsAsync(new IdempotencyResponse
            {
                IdempotencyKey = idempotencyKey,
                Status = IdempotencyStatus.Success,
                SuccessResult = resultData,
                StoredAt = _timeProvider.GetUtcNow(),
                ExpiresAt = _timeProvider.GetUtcNow().AddHours(1)
            });

        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful(data: resultData));

        // Act
        var result1 = await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        var result2 = await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(resultData, result1.Data);
        Assert.Equal(resultData, result2.Data);
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DifferentMessages_ProcessesBothIndependently()
    {
        // Arrange
        var decorator = CreateDecorator();
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var message2 = new TestMessage { MessageId = Guid.NewGuid() };
        var context = new ProcessingContext();

        _keyGeneratorMock.Setup(g => g.GenerateKey(message1, context)).Returns("key1");
        _keyGeneratorMock.Setup(g => g.GenerateKey(message2, context)).Returns("key2");
        _storeMock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyResponse?)null);
        _innerMock.Setup(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await decorator.ProcessAsync(message1, context, TestContext.Current.CancellationToken);
        await decorator.ProcessAsync(message2, context, TestContext.Current.CancellationToken);

        // Assert
        _innerMock.Verify(p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _storeMock.Verify(s => s.StoreSuccessAsync("key1", It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.StoreSuccessAsync("key2", It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
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
