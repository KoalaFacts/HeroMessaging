using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

/// <summary>
/// Unit tests for InMemoryIdempotencyStore
/// Tests in-memory storage implementation for idempotency responses
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryIdempotencyStoreTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryIdempotencyStore _store;

    public InMemoryIdempotencyStoreTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _store = new InMemoryIdempotencyStore(_timeProvider);
    }

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var key = "non-existent-key";

        // Act
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithExpiredEntry_ReturnsNull()
    {
        // Arrange
        var key = "expired-key";
        var data = new { Message = "Success" };
        var ttl = TimeSpan.FromMinutes(10);

        await _store.StoreSuccessAsync(key, data, ttl);

        // Act - advance time past TTL
        _timeProvider.Advance(TimeSpan.FromMinutes(11));
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithValidEntry_ReturnsEntry()
    {
        // Arrange
        var key = "valid-key";
        var data = new { Message = "Success" };
        var ttl = TimeSpan.FromMinutes(10);

        await _store.StoreSuccessAsync(key, data, ttl);

        // Act
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(key, result.IdempotencyKey);
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.GetAsync(null!).AsTask());
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.GetAsync(string.Empty).AsTask());
    }

    #endregion

    #region StoreSuccessAsync Tests

    [Fact]
    public async Task StoreSuccessAsync_WithValidData_StoresSuccessfully()
    {
        // Arrange
        var key = "test-key";
        var data = new { Message = "Success", Count = 42 };
        var ttl = TimeSpan.FromHours(1);

        // Act
        await _store.StoreSuccessAsync(key, data, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Success, result.Status);
        Assert.Equal(data, result.SuccessResult);
        Assert.Null(result.FailureType);
        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithNullData_StoresSuccessfully()
    {
        // Arrange
        var key = "test-key-null";
        var ttl = TimeSpan.FromHours(1);

        // Act
        await _store.StoreSuccessAsync(key, null, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Success, result.Status);
        Assert.Null(result.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccessAsync_SetsCorrectTimestamps()
    {
        // Arrange
        var key = "timestamp-key";
        var data = "test";
        var ttl = TimeSpan.FromHours(2);
        var expectedStoredAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expectedExpiresAt = expectedStoredAt.Add(ttl);

        // Act
        await _store.StoreSuccessAsync(key, data, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStoredAt, result.StoredAt);
        Assert.Equal(expectedExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task StoreSuccessAsync_OverwritesExistingEntry()
    {
        // Arrange
        var key = "overwrite-key";
        var originalData = "original";
        var newData = "updated";
        var ttl = TimeSpan.FromHours(1);

        await _store.StoreSuccessAsync(key, originalData, ttl);

        // Act
        await _store.StoreSuccessAsync(key, newData, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newData, result.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.StoreSuccessAsync(null!, "data", TimeSpan.FromHours(1)).AsTask());
    }

    #endregion

    #region StoreFailureAsync Tests

    [Fact]
    public async Task StoreFailureAsync_WithException_StoresFailureDetails()
    {
        // Arrange
        var key = "failure-key";

        // Create exception with stack trace by throwing and catching it
        Exception exception;
        try
        {
            throw new InvalidOperationException("Test error message");
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        var ttl = TimeSpan.FromMinutes(30);

        // Act
        await _store.StoreFailureAsync(key, exception, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.FailureType);
        Assert.Equal("Test error message", result.FailureMessage);
        Assert.NotNull(result.FailureStackTrace);
        Assert.Null(result.SuccessResult);
    }

    [Fact]
    public async Task StoreFailureAsync_WithInnerException_CapturesDetails()
    {
        // Arrange
        var key = "inner-exception-key";
        var innerException = new ArgumentException("Inner error");
        var exception = new InvalidOperationException("Outer error", innerException);
        var ttl = TimeSpan.FromMinutes(30);

        // Act
        await _store.StoreFailureAsync(key, exception, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Contains("Outer error", result.FailureMessage);
    }

    [Fact]
    public async Task StoreFailureAsync_SetsCorrectTimestamps()
    {
        // Arrange
        var key = "failure-timestamp-key";
        var exception = new Exception("Test");
        var ttl = TimeSpan.FromMinutes(15);
        var expectedStoredAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expectedExpiresAt = expectedStoredAt.Add(ttl);

        // Act
        await _store.StoreFailureAsync(key, exception, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedStoredAt, result.StoredAt);
        Assert.Equal(expectedExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task StoreFailureAsync_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.StoreFailureAsync("key", null!, TimeSpan.FromHours(1)).AsTask());
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var key = "existing-key";
        await _store.StoreSuccessAsync(key, "data", TimeSpan.FromHours(1));

        // Act
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var key = "non-existent-key";

        // Act
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_ReturnsFalse()
    {
        // Arrange
        var key = "expired-key";
        await _store.StoreSuccessAsync(key, "data", TimeSpan.FromMinutes(5));

        // Advance time past expiration
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region CleanupExpiredAsync Tests

    [Fact]
    public async Task CleanupExpiredAsync_WithNoEntries_ReturnsZero()
    {
        // Act
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(0, removedCount);
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithOnlyValidEntries_ReturnsZero()
    {
        // Arrange
        await _store.StoreSuccessAsync("key1", "data1", TimeSpan.FromHours(1));
        await _store.StoreSuccessAsync("key2", "data2", TimeSpan.FromHours(2));

        // Act
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(0, removedCount);
        Assert.True(await _store.ExistsAsync("key1"));
        Assert.True(await _store.ExistsAsync("key2"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesOnlyExpiredEntries()
    {
        // Arrange
        await _store.StoreSuccessAsync("expired1", "data1", TimeSpan.FromMinutes(5));
        await _store.StoreSuccessAsync("expired2", "data2", TimeSpan.FromMinutes(10));
        await _store.StoreSuccessAsync("valid1", "data3", TimeSpan.FromHours(1));
        await _store.StoreSuccessAsync("valid2", "data4", TimeSpan.FromHours(2));

        // Advance time to expire first two entries
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        // Act
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(2, removedCount);
        Assert.False(await _store.ExistsAsync("expired1"));
        Assert.False(await _store.ExistsAsync("expired2"));
        Assert.True(await _store.ExistsAsync("valid1"));
        Assert.True(await _store.ExistsAsync("valid2"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithAllExpiredEntries_RemovesAll()
    {
        // Arrange
        await _store.StoreSuccessAsync("key1", "data1", TimeSpan.FromMinutes(5));
        await _store.StoreSuccessAsync("key2", "data2", TimeSpan.FromMinutes(10));
        await _store.StoreSuccessAsync("key3", "data3", TimeSpan.FromMinutes(15));

        // Advance time past all entries
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        // Act
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(3, removedCount);
        Assert.False(await _store.ExistsAsync("key1"));
        Assert.False(await _store.ExistsAsync("key2"));
        Assert.False(await _store.ExistsAsync("key3"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_CalledMultipleTimes_IsIdempotent()
    {
        // Arrange
        await _store.StoreSuccessAsync("expired", "data", TimeSpan.FromMinutes(5));
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act
        var firstCleanup = await _store.CleanupExpiredAsync();
        var secondCleanup = await _store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(1, firstCleanup);
        Assert.Equal(0, secondCleanup);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentWrites_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - multiple concurrent writes to different keys
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await _store.StoreSuccessAsync($"key-{index}", index, TimeSpan.FromHours(1));
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - all entries stored correctly
        for (int i = 0; i < 100; i++)
        {
            var result = await _store.GetAsync($"key-{i}");
            Assert.NotNull(result);
            Assert.Equal(i, result.SuccessResult);
        }
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_ThreadSafe()
    {
        // Arrange
        await _store.StoreSuccessAsync("shared-key", "initial", TimeSpan.FromHours(1));
        var tasks = new List<Task>();

        // Act - concurrent reads and writes to the same key
        for (int i = 0; i < 50; i++)
        {
            // Read task
            tasks.Add(Task.Run(async () =>
            {
                var result = await _store.GetAsync("shared-key");
                Assert.NotNull(result);
            }));

            // Write task
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await _store.StoreSuccessAsync("shared-key", $"update-{index}", TimeSpan.FromHours(1));
            }));
        }

        // Assert - no exceptions thrown
        await Task.WhenAll(tasks);

        // Verify final state exists
        var finalResult = await _store.GetAsync("shared-key");
        Assert.NotNull(finalResult);
    }

    [Fact]
    public async Task ConcurrentCleanup_ThreadSafe()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            await _store.StoreSuccessAsync($"key-{i}", i, TimeSpan.FromMinutes(5));
        }

        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act - multiple concurrent cleanups
        var cleanupTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => _store.CleanupExpiredAsync().AsTask()))
            .ToList();

        var results = await Task.WhenAll(cleanupTasks);

        // Assert - total removed should equal 50 (entries may be distributed across cleanup calls)
        var totalRemoved = results.Sum();
        Assert.Equal(50, totalRemoved);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task StoreSuccessAsync_WithLargeObject_StoresSuccessfully()
    {
        // Arrange
        var key = "large-object-key";
        var largeData = new
        {
            Items = Enumerable.Range(0, 1000).Select(i => new { Id = i, Name = $"Item {i}" }).ToList()
        };
        var ttl = TimeSpan.FromHours(1);

        // Act
        await _store.StoreSuccessAsync(key, largeData, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(largeData, result.SuccessResult);
    }

    [Fact]
    public async Task GetAsync_JustBeforeExpiration_ReturnsEntry()
    {
        // Arrange
        var key = "about-to-expire-key";
        var ttl = TimeSpan.FromMinutes(10);
        await _store.StoreSuccessAsync(key, "data", ttl);

        // Act - advance to 1 second before expiration
        _timeProvider.Advance(TimeSpan.FromMinutes(10).Subtract(TimeSpan.FromSeconds(1)));
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetAsync_ExactlyAtExpiration_ReturnsNull()
    {
        // Arrange
        var key = "exact-expiration-key";
        var ttl = TimeSpan.FromMinutes(10);
        await _store.StoreSuccessAsync(key, "data", ttl);

        // Act - advance to exact expiration time
        _timeProvider.Advance(ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreFailureAsync_WithExceptionWithoutStackTrace_StoresSuccessfully()
    {
        // Arrange
        var key = "no-stack-trace-key";
        var exception = new Exception("Error without stack trace");
        var ttl = TimeSpan.FromMinutes(15);

        // Act
        await _store.StoreFailureAsync(key, exception, ttl);
        var result = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Equal("Error without stack trace", result.FailureMessage);
    }

    #endregion
}
