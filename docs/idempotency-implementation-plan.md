# HeroMessaging Idempotency Framework - Implementation Plan

**Version**: 1.0
**Date**: 2025-11-06
**Status**: In Progress
**Related ADR**: [ADR 0005: Idempotency Framework](adr/0005-idempotency-framework.md)

## Executive Summary

This document provides a detailed, step-by-step implementation plan for adding comprehensive idempotency support to HeroMessaging. The implementation follows Test-Driven Development (TDD) principles and the constitutional requirements outlined in [CLAUDE.md](../CLAUDE.md).

**Goals**:
- Provide exactly-once processing semantics for at-least-once message delivery
- Maintain <1ms overhead for message processing
- Achieve 80%+ test coverage
- Support multiple storage backends (in-memory, SQL Server, PostgreSQL)
- Maintain backward compatibility

**Timeline**: 4 weeks (20 business days)
**Effort Estimate**: ~80-100 hours

## Constitutional Compliance Checklist

Before starting implementation, verify alignment with constitutional principles:

- [x] **Code Quality & Maintainability**: Using decorator pattern consistent with existing architecture
- [x] **Testing Excellence**: TDD approach, 80%+ coverage target, Xunit.v3 exclusively
- [x] **User Experience Consistency**: Fluent API, actionable error messages
- [x] **Performance & Efficiency**: <1ms overhead target, ValueTask-based, zero-allocation paths
- [x] **Architectural Governance**: ADR documented, plugin architecture preserved
- [x] **Task Verification Protocol**: Each task includes verification steps

## Phase 1: Foundation (Week 1, Days 1-5)

**Goal**: Create core abstractions and basic implementation with high test coverage

### Task 1.1: Create Idempotency Abstractions (Day 1, 4 hours)

**Objective**: Define core interfaces in `HeroMessaging.Abstractions`

**TDD Steps**:
1. Write interface definitions first (contract-first design)
2. Create XML documentation for all public APIs
3. Add to solution and verify compilation

**Files to Create**:
```
src/HeroMessaging.Abstractions/
└── Idempotency/
    ├── IIdempotencyStore.cs          (Core storage interface)
    ├── IIdempotencyKeyGenerator.cs   (Key generation strategy)
    ├── IIdempotencyPolicy.cs         (Configuration policy)
    ├── IdempotencyResponse.cs        (Cached response model)
    └── IdempotencyStatus.cs          (Enum: Success/Failure/Processing)
```

**Implementation Details**:

**IIdempotencyStore.cs**:
```csharp
namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Provides storage for idempotency responses to enable exactly-once processing semantics.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retrieves a cached idempotency response if one exists.
    /// </summary>
    /// <param name="idempotencyKey">The unique idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached response if found; otherwise null.</returns>
    ValueTask<IdempotencyResponse?> GetAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a successful processing result.
    /// </summary>
    ValueTask StoreSuccessAsync(
        string idempotencyKey,
        object? result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a failed processing result.
    /// </summary>
    ValueTask StoreFailureAsync(
        string idempotencyKey,
        Exception exception,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an idempotency key exists in the store.
    /// </summary>
    ValueTask<bool> ExistsAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired idempotency entries from the store.
    /// </summary>
    ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
```

**IIdempotencyKeyGenerator.cs**:
```csharp
namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Generates idempotency keys for messages.
/// </summary>
public interface IIdempotencyKeyGenerator
{
    /// <summary>
    /// Generates an idempotency key from a message and processing context.
    /// </summary>
    /// <param name="message">The message to generate a key for.</param>
    /// <param name="context">The processing context.</param>
    /// <returns>A unique idempotency key.</returns>
    string GenerateKey(IMessage message, ProcessingContext context);
}
```

**IIdempotencyPolicy.cs**:
```csharp
namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Defines policy for idempotency behavior.
/// </summary>
public interface IIdempotencyPolicy
{
    /// <summary>
    /// Gets the time-to-live for successful responses.
    /// </summary>
    TimeSpan SuccessTtl { get; }

    /// <summary>
    /// Gets the time-to-live for failed responses.
    /// </summary>
    TimeSpan FailureTtl { get; }

    /// <summary>
    /// Gets whether to cache failed processing attempts.
    /// </summary>
    bool CacheFailures { get; }

    /// <summary>
    /// Gets the key generation strategy.
    /// </summary>
    IIdempotencyKeyGenerator KeyGenerator { get; }

    /// <summary>
    /// Determines if an exception represents an idempotent failure that should be cached.
    /// </summary>
    bool IsIdempotentFailure(Exception exception);
}
```

**IdempotencyResponse.cs**:
```csharp
namespace HeroMessaging.Abstractions.Idempotency;

/// <summary>
/// Represents a cached idempotency response.
/// </summary>
public sealed class IdempotencyResponse
{
    public required string IdempotencyKey { get; init; }
    public object? SuccessResult { get; init; }
    public string? FailureType { get; init; }
    public string? FailureMessage { get; init; }
    public string? FailureStackTrace { get; init; }
    public DateTime StoredAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public IdempotencyStatus Status { get; init; }
}

/// <summary>
/// Status of an idempotency entry.
/// </summary>
public enum IdempotencyStatus
{
    /// <summary>Processing completed successfully.</summary>
    Success = 0,

    /// <summary>Processing failed with an idempotent error.</summary>
    Failure = 1,

    /// <summary>Processing is currently in progress (lock).</summary>
    Processing = 2
}
```

**Verification Steps**:
```bash
# 1. Build the project
dotnet build src/HeroMessaging.Abstractions

# 2. Verify no errors or warnings
# 3. Verify XML documentation generated
ls src/HeroMessaging.Abstractions/bin/Debug/*/HeroMessaging.Abstractions.xml

# 4. Check namespace consistency
grep -r "namespace HeroMessaging.Abstractions.Idempotency" src/HeroMessaging.Abstractions/Idempotency/
```

**Success Criteria**:
- [ ] All interfaces compile without errors
- [ ] XML documentation complete for all public members
- [ ] Namespace follows convention: `HeroMessaging.Abstractions.Idempotency`
- [ ] No nullable reference warnings

---

### Task 1.2: Implement MessageIdKeyGenerator (Day 1, 2 hours)

**Objective**: Create default key generator implementation (TDD)

**TDD Steps**:
1. **Write test first**: `MessageIdKeyGeneratorTests.cs`
2. **Watch it fail**: Verify test fails (red)
3. **Implement**: Create `MessageIdKeyGenerator.cs`
4. **Make it pass**: Verify test passes (green)
5. **Refactor**: Optimize if needed

**Test File** (`tests/HeroMessaging.Tests/Unit/Idempotency/MessageIdKeyGeneratorTests.cs`):
```csharp
namespace HeroMessaging.Tests.Unit.Idempotency;

public sealed class MessageIdKeyGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithValidMessage_ReturnsKeyWithMessageId()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = ProcessingContext.Default;
        var generator = new MessageIdKeyGenerator();

        // Act
        var key = generator.GenerateKey(message, context);

        // Assert
        Assert.StartsWith("idempotency:", key);
        Assert.Contains(messageId.ToString(), key);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithSameMessage_ReturnsSameKey()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };
        var context = ProcessingContext.Default;
        var generator = new MessageIdKeyGenerator();

        // Act
        var key1 = generator.GenerateKey(message, context);
        var key2 = generator.GenerateKey(message, context);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GenerateKey_WithDifferentMessages_ReturnsDifferentKeys()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid() };
        var message2 = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
        var generator = new MessageIdKeyGenerator();

        // Act
        var key1 = generator.GenerateKey(message1, context);
        var key2 = generator.GenerateKey(message2, context);

        // Assert
        Assert.NotEqual(key1, key2);
    }
}
```

**Implementation File** (`src/HeroMessaging/Idempotency/KeyGeneration/MessageIdKeyGenerator.cs`):
```csharp
namespace HeroMessaging.Idempotency.KeyGeneration;

/// <summary>
/// Generates idempotency keys based on message IDs.
/// </summary>
public sealed class MessageIdKeyGenerator : IIdempotencyKeyGenerator
{
    private const string Prefix = "idempotency";

    /// <inheritdoc />
    public string GenerateKey(IMessage message, ProcessingContext context)
    {
        ArgumentNullException.ThrowIfNull(message);
        return $"{Prefix}:{message.MessageId}";
    }
}
```

**Verification Steps**:
```bash
# 1. Run tests (should fail initially - RED)
dotnet test --filter "FullyQualifiedName~MessageIdKeyGeneratorTests" --verbosity normal

# 2. Implement MessageIdKeyGenerator

# 3. Run tests again (should pass - GREEN)
dotnet test --filter "FullyQualifiedName~MessageIdKeyGeneratorTests" --verbosity normal

# 4. Verify coverage
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~MessageIdKeyGeneratorTests"
```

**Success Criteria**:
- [ ] All tests pass (green)
- [ ] Test coverage: 100% for `MessageIdKeyGenerator`
- [ ] No allocations in hot path (use BenchmarkDotNet to verify)

---

### Task 1.3: Implement InMemoryIdempotencyStore (Day 2, 6 hours)

**Objective**: Create in-memory storage implementation with TDD

**TDD Steps**:
1. **Write comprehensive tests** covering all scenarios
2. **Watch them fail** (red)
3. **Implement** minimal code to pass
4. **Refactor** for performance and clarity

**Test File** (`tests/HeroMessaging.Tests/Unit/Idempotency/InMemoryIdempotencyStoreTests.cs`):
```csharp
namespace HeroMessaging.Tests.Unit.Idempotency;

public sealed class InMemoryIdempotencyStoreTests
{
    private readonly TimeProvider _timeProvider;
    private readonly InMemoryIdempotencyStore _store;

    public InMemoryIdempotencyStoreTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _store = new InMemoryIdempotencyStore(_timeProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var key = "test-key";

        // Act
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StoreSuccessAsync_ThenGet_ReturnsStoredResponse()
    {
        // Arrange
        var key = "test-key";
        var data = new { Message = "Success" };
        var ttl = TimeSpan.FromMinutes(10);

        // Act
        await _store.StoreSuccessAsync(key, data, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Success, result.Status);
        Assert.Equal(data, result.SuccessResult);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StoreFailureAsync_ThenGet_ReturnsStoredFailure()
    {
        // Arrange
        var key = "test-key";
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromMinutes(10);

        // Act
        await _store.StoreFailureAsync(key, exception, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Equal(exception.GetType().FullName, result.FailureType);
        Assert.Equal(exception.Message, result.FailureMessage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_AfterTtlExpired_ReturnsNull()
    {
        // Arrange
        var key = "test-key";
        var data = new { Message = "Success" };
        var ttl = TimeSpan.FromMinutes(10);
        await _store.StoreSuccessAsync(key, data, ttl);

        // Act - advance time past TTL
        var fakeTimeProvider = (FakeTimeProvider)_timeProvider;
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(11));
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var key = "test-key";
        await _store.StoreSuccessAsync(key, "data", TimeSpan.FromMinutes(10));

        // Act
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CleanupExpiredAsync_RemovesExpiredEntries()
    {
        // Arrange
        var key1 = "expired-key";
        var key2 = "valid-key";
        await _store.StoreSuccessAsync(key1, "data1", TimeSpan.FromMinutes(5));
        await _store.StoreSuccessAsync(key2, "data2", TimeSpan.FromMinutes(20));

        // Advance time to expire first entry
        var fakeTimeProvider = (FakeTimeProvider)_timeProvider;
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(1, removedCount);
        Assert.Null(await _store.GetAsync(key1));
        Assert.NotNull(await _store.GetAsync(key2));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var key = "concurrent-key";
        var tasks = new List<Task>();

        // Act - multiple concurrent writes
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await _store.StoreSuccessAsync($"{key}-{index}", index, TimeSpan.FromMinutes(10));
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - all entries stored correctly
        for (int i = 0; i < 100; i++)
        {
            var result = await _store.GetAsync($"{key}-{i}");
            Assert.NotNull(result);
            Assert.Equal(i, result.SuccessResult);
        }
    }
}
```

**Implementation File** (`src/HeroMessaging/Idempotency/Storage/InMemoryIdempotencyStore.cs`):
```csharp
namespace HeroMessaging.Idempotency.Storage;

/// <summary>
/// In-memory implementation of <see cref="IIdempotencyStore"/> for testing and non-persistent scenarios.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyResponse> _cache = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryIdempotencyStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<IdempotencyResponse?> GetAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(idempotencyKey, out var response))
        {
            // Check if expired
            if (_timeProvider.GetUtcNow() >= response.ExpiresAt)
            {
                _cache.TryRemove(idempotencyKey, out _);
                return ValueTask.FromResult<IdempotencyResponse?>(null);
            }
            return ValueTask.FromResult<IdempotencyResponse?>(response);
        }
        return ValueTask.FromResult<IdempotencyResponse?>(null);
    }

    public ValueTask StoreSuccessAsync(
        string idempotencyKey,
        object? result,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var response = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            SuccessResult = result,
            Status = IdempotencyStatus.Success,
            StoredAt = now,
            ExpiresAt = now.Add(ttl)
        };
        _cache[idempotencyKey] = response;
        return ValueTask.CompletedTask;
    }

    public ValueTask StoreFailureAsync(
        string idempotencyKey,
        Exception exception,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var response = new IdempotencyResponse
        {
            IdempotencyKey = idempotencyKey,
            FailureType = exception.GetType().FullName,
            FailureMessage = exception.Message,
            FailureStackTrace = exception.StackTrace,
            Status = IdempotencyStatus.Failure,
            StoredAt = now,
            ExpiresAt = now.Add(ttl)
        };
        _cache[idempotencyKey] = response;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ExistsAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var exists = _cache.ContainsKey(idempotencyKey);
        return ValueTask.FromResult(exists);
    }

    public ValueTask<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiredKeys = _cache
            .Where(kvp => now >= kvp.Value.ExpiresAt)
            .Select(kvp => kvp.Key)
            .ToList();

        var removedCount = 0;
        foreach (var key in expiredKeys)
        {
            if (_cache.TryRemove(key, out _))
            {
                removedCount++;
            }
        }
        return ValueTask.FromResult(removedCount);
    }
}
```

**Verification Steps**:
```bash
# 1. Run tests (RED)
dotnet test --filter "FullyQualifiedName~InMemoryIdempotencyStoreTests" --verbosity normal

# 2. Implement InMemoryIdempotencyStore

# 3. Run tests (GREEN)
dotnet test --filter "FullyQualifiedName~InMemoryIdempotencyStoreTests" --verbosity normal

# 4. Verify coverage
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~InMemoryIdempotencyStoreTests"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report

# 5. Check coverage report
open coverage-report/index.html
# Target: 100% coverage for InMemoryIdempotencyStore
```

**Success Criteria**:
- [ ] All tests pass
- [ ] 100% code coverage for `InMemoryIdempotencyStore`
- [ ] Thread-safety verified with concurrent test
- [ ] TTL expiration works correctly

---

### Task 1.4: Implement IdempotencyDecorator (Day 3-4, 8 hours)

**Objective**: Create the core decorator that integrates idempotency into the pipeline

**TDD Steps**: Write comprehensive tests first, then implement

**Test File** (`tests/HeroMessaging.Tests/Unit/Idempotency/IdempotencyDecoratorTests.cs`):
```csharp
namespace HeroMessaging.Tests.Unit.Idempotency;

public sealed class IdempotencyDecoratorTests
{
    private readonly Mock<IMessageProcessor> _innerProcessorMock;
    private readonly Mock<IIdempotencyStore> _storeMock;
    private readonly Mock<IIdempotencyPolicy> _policyMock;
    private readonly Mock<IIdempotencyKeyGenerator> _keyGeneratorMock;
    private readonly Mock<ILogger<IdempotencyDecorator>> _loggerMock;
    private readonly TimeProvider _timeProvider;
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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_CacheHit_ReturnsFromCacheWithoutInvokingInner()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
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
            StoredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
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
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_CacheMiss_InvokesInnerAndStoresSuccess()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
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
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_IdempotentFailure_StoresFailure()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
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
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_NonIdempotentFailure_DoesNotStoreFailure()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
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
    [Trait("Category", "Unit")]
    public async Task ProcessAsync_CachedFailure_ReturnsFromCache()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var context = ProcessingContext.Default;
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
            StoredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        _storeMock
            .Setup(s => s.GetAsync(idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _decorator.ProcessAsync(message, context);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Exception);
        Assert.Contains("Cached error", result.Exception.Message);

        _innerProcessorMock.Verify(
            p => p.ProcessAsync(It.IsAny<IMessage>(), It.IsAny<ProcessingContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

**Implementation** - See ADR for full decorator implementation.

**Verification Steps**:
```bash
# 1. Run tests (RED)
dotnet test --filter "FullyQualifiedName~IdempotencyDecoratorTests" --verbosity normal

# 2. Implement IdempotencyDecorator

# 3. Run tests (GREEN)
dotnet test --filter "FullyQualifiedName~IdempotencyDecoratorTests" --verbosity normal

# 4. Verify coverage (target: 90%+)
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~IdempotencyDecoratorTests"
```

**Success Criteria**:
- [ ] All tests pass
- [ ] 90%+ coverage for `IdempotencyDecorator`
- [ ] Cache hit/miss logic works correctly
- [ ] Failure classification works

---

### Task 1.5: Implement DefaultIdempotencyPolicy (Day 4, 2 hours)

**Objective**: Create default policy implementation with error classification

**Files**:
- Implementation: `src/HeroMessaging/Idempotency/DefaultIdempotencyPolicy.cs`
- Tests: `tests/HeroMessaging.Tests/Unit/Idempotency/DefaultIdempotencyPolicyTests.cs`

**Key Logic** (error classification):
```csharp
public bool IsIdempotentFailure(Exception exception) => exception switch
{
    ValidationException => true,
    ArgumentException => true,
    InvalidOperationException => true,
    UnauthorizedAccessException => true,

    TimeoutException => false,
    TaskCanceledException => false,
    OperationCanceledException => false,

    _ => false // Conservative: don't cache unknown errors
};
```

**Success Criteria**:
- [ ] Tests cover all error types
- [ ] 100% coverage
- [ ] Policy is configurable via constructor

---

### Task 1.6: Phase 1 Validation (Day 5, 2 hours)

**Objective**: Verify Phase 1 deliverables meet constitutional requirements

**Checklist**:
- [ ] Build succeeds: `dotnet build --no-restore`
- [ ] All tests pass: `dotnet test --filter Category=Unit`
- [ ] Coverage ≥ 80%: Check coverage report
- [ ] No warnings: Build output clean
- [ ] Performance: Basic benchmarks (optional for Phase 1)
- [ ] Documentation: XML docs complete

**Commands**:
```bash
# Full validation suite
dotnet build
dotnet test --filter Category=Unit
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

---

## Phase 2: Integration (Week 2, Days 6-10)

**Goal**: Integrate idempotency into HeroMessaging pipeline and add storage adapters

### Task 2.1: Create IdempotencyBuilder (Day 6, 4 hours)
- Fluent configuration API
- Service registration
- Decorator registration in pipeline

### Task 2.2: Implement SQL Server Storage (Day 7-8, 8 hours)
- Database schema
- `SqlServerIdempotencyStore` implementation
- Integration tests with real database
- Schema migration scripts

### Task 2.3: Implement PostgreSQL Storage (Day 9, 4 hours)
- PostgreSQL-specific implementation
- Integration tests
- Schema migration scripts

### Task 2.4: End-to-End Integration Tests (Day 10, 4 hours)
- Test full pipeline with idempotency enabled
- Test with inbox/outbox patterns
- Test multi-storage scenarios

---

## Phase 3: Advanced Features (Week 3, Days 11-15)

**Goal**: Add advanced key generation, concurrency handling, and performance optimization

### Task 3.1: Content Hash Key Generator (Day 11, 4 hours)
- SHA256-based content hashing
- Performance benchmarks
- Tests for hash stability

### Task 3.2: Composite Key Generator (Day 12, 4 hours)
- Multi-field key composition
- Tenant-aware keys
- Tests for key uniqueness

### Task 3.3: Concurrency Handling (Day 13, 4 hours)
- Processing locks to prevent duplicate execution
- Timeout handling for stalled locks
- Tests for concurrent requests

### Task 3.4: Background Cleanup Task (Day 14, 4 hours)
- `BackgroundService` for expired entry cleanup
- Configurable cleanup interval
- Performance tests

### Task 3.5: Performance Benchmarks (Day 15, 4 hours)
- BenchmarkDotNet suite
- Latency tests (cache hit/miss)
- Throughput tests
- Memory allocation tests

---

## Phase 4: Documentation & Polish (Week 4, Days 16-20)

**Goal**: Complete documentation, examples, and final polish

### Task 4.1: User Documentation (Day 16-17, 8 hours)
- Getting started guide
- Configuration examples
- Best practices
- Troubleshooting guide

### Task 4.2: Sample Projects (Day 18, 4 hours)
- Example: Financial transaction processing
- Example: External API integration
- Example: Multi-tenant scenario

### Task 4.3: Performance Optimization (Day 19, 4 hours)
- Profile hot paths
- Optimize allocations
- Verify <1ms overhead target

### Task 4.4: Final Validation & PR (Day 20, 4 hours)
- Full constitutional compliance check
- Update CLAUDE.md
- Create PR with comprehensive description
- CI/CD verification

---

## Success Metrics

### Performance Targets
| Metric | Target | Validation Method |
|--------|--------|-------------------|
| Cache Hit Latency (p99) | <0.5ms | BenchmarkDotNet |
| Cache Miss Overhead (p99) | <1ms | BenchmarkDotNet |
| Throughput | >100K msg/s | Benchmarks |
| Memory per Entry | <1KB | Memory profiler |
| Storage Cleanup | <100ms for 10K entries | Integration tests |

### Quality Targets
| Metric | Target | Validation Method |
|--------|--------|-------------------|
| Test Coverage | ≥80% | Coverlet |
| Unit Test Count | ≥50 | Test explorer |
| Integration Test Count | ≥20 | Test explorer |
| Performance Tests | ≥10 | BenchmarkDotNet |
| Documentation Coverage | 100% public APIs | XML doc validation |

### Functional Requirements
- [ ] Idempotent success responses cached and retrievable
- [ ] Idempotent failures cached per policy
- [ ] Multiple key generation strategies
- [ ] TTL expiration works
- [ ] Concurrent request handling
- [ ] All storage providers work (in-memory, SQL Server, PostgreSQL)
- [ ] Pipeline integration seamless
- [ ] Backward compatible

---

## Risk Mitigation

| Risk | Mitigation Strategy |
|------|---------------------|
| **Performance Regression** | Continuous benchmarks in CI, <10% tolerance |
| **Storage Exhaustion** | Automatic cleanup, configurable TTLs, monitoring |
| **Breaking Changes** | Opt-in API, comprehensive tests, semantic versioning |
| **Test Coverage Gap** | TDD approach, coverage gates in CI |
| **Integration Issues** | Integration tests with real dependencies |

---

## Dependencies & Prerequisites

**Tools Required**:
- .NET SDK 6.0+ (multi-targeting: netstandard2.0, net6.0-net10.0)
- SQL Server LocalDB or Docker (for SQL Server tests)
- PostgreSQL Docker (for PostgreSQL tests)
- BenchmarkDotNet
- Coverlet + ReportGenerator

**Setup Commands**:
```bash
# Install tools
dotnet tool install --global coverlet.console
dotnet tool install --global dotnet-reportgenerator-globaltool

# Start PostgreSQL (Docker)
docker run --name postgres-test -e POSTGRES_PASSWORD=test -p 5432:5432 -d postgres

# Start SQL Server (Docker)
docker run --name sqlserver-test -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Test123!" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

---

## Daily Workflow (TDD)

Each task follows this workflow:

1. **Morning** (Planning)
   - Review task objectives
   - Write test cases (RED)
   - Verify tests fail

2. **Midday** (Implementation)
   - Implement minimal code (GREEN)
   - Run tests continuously
   - Refactor for quality

3. **Afternoon** (Validation)
   - Run full test suite
   - Check coverage
   - Update TODO list
   - Commit changes

4. **End of Day** (Review)
   - Verify constitutional compliance
   - Update plan document
   - Prepare next day's tasks

---

## Progress Tracking

**Week 1** (Foundation):
- [ ] Task 1.1: Abstractions
- [ ] Task 1.2: MessageIdKeyGenerator
- [ ] Task 1.3: InMemoryIdempotencyStore
- [ ] Task 1.4: IdempotencyDecorator
- [ ] Task 1.5: DefaultIdempotencyPolicy
- [ ] Task 1.6: Phase 1 Validation

**Week 2** (Integration):
- [ ] Task 2.1: IdempotencyBuilder
- [ ] Task 2.2: SQL Server Storage
- [ ] Task 2.3: PostgreSQL Storage
- [ ] Task 2.4: End-to-End Tests

**Week 3** (Advanced):
- [ ] Task 3.1: Content Hash Generator
- [ ] Task 3.2: Composite Key Generator
- [ ] Task 3.3: Concurrency Handling
- [ ] Task 3.4: Background Cleanup
- [ ] Task 3.5: Performance Benchmarks

**Week 4** (Documentation):
- [ ] Task 4.1: User Documentation
- [ ] Task 4.2: Sample Projects
- [ ] Task 4.3: Performance Optimization
- [ ] Task 4.4: Final Validation & PR

---

## Appendix: File Structure

```
src/
├── HeroMessaging.Abstractions/
│   └── Idempotency/
│       ├── IIdempotencyStore.cs
│       ├── IIdempotencyKeyGenerator.cs
│       ├── IIdempotencyPolicy.cs
│       ├── IdempotencyResponse.cs
│       └── IdempotencyStatus.cs
│
├── HeroMessaging/
│   └── Idempotency/
│       ├── Decorators/
│       │   └── IdempotencyDecorator.cs
│       ├── KeyGeneration/
│       │   ├── MessageIdKeyGenerator.cs
│       │   ├── ContentHashKeyGenerator.cs
│       │   └── CompositeKeyGenerator.cs
│       ├── Storage/
│       │   ├── InMemoryIdempotencyStore.cs
│       │   └── IdempotencyCleanupService.cs
│       ├── Configuration/
│       │   ├── IdempotencyBuilder.cs
│       │   └── ExtensionsToIHeroMessagingBuilder.cs
│       └── DefaultIdempotencyPolicy.cs
│
├── HeroMessaging.Storage.SqlServer/
│   └── Idempotency/
│       ├── SqlServerIdempotencyStore.cs
│       └── Migrations/
│           └── 0001_CreateIdempotencyStore.sql
│
└── HeroMessaging.Storage.PostgreSql/
    └── Idempotency/
        ├── PostgreSqlIdempotencyStore.cs
        └── Migrations/
            └── 0001_create_idempotency_store.sql

tests/
├── HeroMessaging.Tests/
│   ├── Unit/Idempotency/
│   │   ├── MessageIdKeyGeneratorTests.cs
│   │   ├── ContentHashKeyGeneratorTests.cs
│   │   ├── InMemoryIdempotencyStoreTests.cs
│   │   ├── IdempotencyDecoratorTests.cs
│   │   └── DefaultIdempotencyPolicyTests.cs
│   └── Integration/Idempotency/
│       ├── IdempotencyEndToEndTests.cs
│       ├── SqlServerIdempotencyStoreTests.cs
│       └── PostgreSqlIdempotencyStoreTests.cs
│
└── HeroMessaging.Benchmarks/
    └── IdempotencyBenchmarks.cs

docs/
├── adr/
│   └── 0005-idempotency-framework.md
├── idempotency-implementation-plan.md (this file)
└── idempotency-user-guide.md (Week 4)
```

---

## Next Steps

1. **Review and Approve**: Team review of ADR and implementation plan
2. **Setup Environment**: Install dependencies, configure databases
3. **Begin Phase 1**: Start with Task 1.1 (Abstractions)
4. **Daily Standups**: Track progress, address blockers
5. **Weekly Reviews**: Validate phase completion

**Questions or concerns?** Review ADR 0005 or raise in team discussion.
