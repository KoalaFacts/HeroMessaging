using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency.Decorators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
public class IdempotencyDecoratorTests
{
    private readonly Mock<IMessageProcessor> _mockInner;
    private readonly Mock<IIdempotencyStore> _mockStore;
    private readonly Mock<IIdempotencyPolicy> _mockPolicy;
    private readonly Mock<IIdempotencyKeyGenerator> _mockKeyGenerator;
    private readonly Mock<ILogger<IdempotencyDecorator>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;
    private readonly IdempotencyDecorator _sut;
    private readonly TestMessage _testMessage;
    private readonly ProcessingContext _context;

    public IdempotencyDecoratorTests()
    {
        _mockInner = new Mock<IMessageProcessor>();
        _mockStore = new Mock<IIdempotencyStore>();
        _mockPolicy = new Mock<IIdempotencyPolicy>();
        _mockKeyGenerator = new Mock<IIdempotencyKeyGenerator>();
        _mockLogger = new Mock<ILogger<IdempotencyDecorator>>();
        _timeProvider = new FakeTimeProvider();

        _mockPolicy.Setup(p => p.KeyGenerator).Returns(_mockKeyGenerator.Object);
        _mockPolicy.Setup(p => p.SuccessTtl).Returns(TimeSpan.FromHours(24));
        _mockPolicy.Setup(p => p.FailureTtl).Returns(TimeSpan.FromHours(1));
        _mockPolicy.Setup(p => p.CacheFailures).Returns(true);

        _sut = new IdempotencyDecorator(
            _mockInner.Object,
            _mockStore.Object,
            _mockPolicy.Object,
            _mockLogger.Object,
            _timeProvider);

        _testMessage = new TestMessage { MessageId = Guid.NewGuid() };
        _context = new ProcessingContext();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new IdempotencyDecorator(
            null!,
            _mockStore.Object,
            _mockPolicy.Object,
            _mockLogger.Object,
            _timeProvider));
        Assert.Equal("inner", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new IdempotencyDecorator(
            _mockInner.Object,
            null!,
            _mockPolicy.Object,
            _mockLogger.Object,
            _timeProvider));
        Assert.Equal("store", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new IdempotencyDecorator(
            _mockInner.Object,
            _mockStore.Object,
            null!,
            _mockLogger.Object,
            _timeProvider));
        Assert.Equal("policy", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new IdempotencyDecorator(
            _mockInner.Object,
            _mockStore.Object,
            _mockPolicy.Object,
            null!,
            _timeProvider));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new IdempotencyDecorator(
            _mockInner.Object,
            _mockStore.Object,
            _mockPolicy.Object,
            _mockLogger.Object,
            null!));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    #endregion

    #region Cache Hit Tests

    [Fact]
    public async Task ProcessAsync_WithCachedSuccess_ReturnsCachedResultWithoutInvokingInner()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var cachedData = new { Value = "cached" };
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            Status = IdempotencyStatus.Success,
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(24),
            SuccessResult = cachedData
        };

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(cachedData, result.Data);
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_ReturnsReconstructedFailure()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            Status = IdempotencyStatus.Failure,
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1),
            FailureType = typeof(InvalidOperationException).AssemblyQualifiedName,
            FailureMessage = "Cached failure message"
        };

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Equal("Cached failure message", result.Exception.Message);
        _mockInner.Verify(i => i.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithCachedFailure_UnknownExceptionType_ReturnsGenericException()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var cachedResponse = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            Status = IdempotencyStatus.Failure,
            StoredAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(1),
            FailureType = "UnknownExceptionType",
            FailureMessage = "Unknown failure message"
        };

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.IsType<Exception>(result.Exception);
        Assert.Contains("UnknownExceptionType", result.Exception.Message);
        Assert.Contains("Unknown failure message", result.Exception.Message);
    }

    #endregion

    #region Cache Miss - Success Tests

    [Fact]
    public async Task ProcessAsync_WithCacheMissAndSuccess_ExecutesInnerAndStoresResult()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var successData = new { Value = "success" };
        var successResult = ProcessingResult.Successful(data: successData);

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(successData, result.Data);
        _mockStore.Verify(s => s.StoreSuccessAsync(
            idempotencyKey,
            successData,
            TimeSpan.FromHours(24),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCacheMissAndSuccessWithNullData_StoresNullSuccessfully()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var successResult = ProcessingResult.Successful();

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.True(result.Success);
        _mockStore.Verify(s => s.StoreSuccessAsync(
            idempotencyKey,
            null,
            TimeSpan.FromHours(24),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cache Miss - Failure Tests

    [Fact]
    public async Task ProcessAsync_WithIdempotentFailureAndCachingEnabled_StoresFailure()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var exception = new InvalidOperationException("Idempotent failure");
        var failureResult = ProcessingResult.Failed(exception, "Failed");

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);
        _mockPolicy.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        _mockStore.Verify(s => s.StoreFailureAsync(
            idempotencyKey,
            exception,
            TimeSpan.FromHours(1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithNonIdempotentFailure_DoesNotStoreFailure()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var exception = new TimeoutException("Transient failure");
        var failureResult = ProcessingResult.Failed(exception, "Failed");

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);
        _mockPolicy.Setup(p => p.IsIdempotentFailure(exception)).Returns(false);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(exception, result.Exception);
        _mockStore.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithFailureAndCachingDisabled_DoesNotStoreFailure()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var exception = new InvalidOperationException("Idempotent failure");
        var failureResult = ProcessingResult.Failed(exception, "Failed");

        _mockPolicy.Setup(p => p.CacheFailures).Returns(false);

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);
        _mockPolicy.Setup(p => p.IsIdempotentFailure(exception)).Returns(true);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        _mockStore.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WithFailureButNullException_DoesNotStoreFailure()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var failureResult = ProcessingResult.Failed(null!, "Failed");

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        // Act
        var result = await _sut.ProcessAsync(_testMessage, _context);

        // Assert
        Assert.False(result.Success);
        _mockStore.Verify(s => s.StoreFailureAsync(
            It.IsAny<string>(),
            It.IsAny<Exception>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ProcessAsync_WithCancellationToken_PassesToStore()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _mockKeyGenerator.Setup(kg => kg.GenerateKey(_testMessage, _context))
            .Returns(idempotencyKey);
        _mockStore.Setup(s => s.GetAsync(idempotencyKey, token))
            .ReturnsAsync((IdempotencyResponse?)null);
        _mockInner.Setup(i => i.ProcessAsync(_testMessage, _context, token))
            .ReturnsAsync(ProcessingResult.Successful());

        // Act
        await _sut.ProcessAsync(_testMessage, _context, token);

        // Assert
        _mockStore.Verify(s => s.GetAsync(idempotencyKey, token), Times.Once);
        _mockStore.Verify(s => s.StoreSuccessAsync(
            idempotencyKey,
            It.IsAny<object?>(),
            It.IsAny<TimeSpan>(),
            token), Times.Once);
    }

    #endregion

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
