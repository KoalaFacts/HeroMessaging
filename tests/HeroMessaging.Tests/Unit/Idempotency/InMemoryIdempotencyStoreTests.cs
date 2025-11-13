using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Idempotency;

[Trait("Category", "Unit")]
public sealed class InMemoryIdempotencyStoreTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidTimeProvider_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InMemoryIdempotencyStore(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region GetAsync - Success Scenarios

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act
        var result = await store.GetAsync("non-existent-key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithExistingSuccessKey_ReturnsResponse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var data = new { Value = 42 };
        var ttl = TimeSpan.FromHours(1);

        await store.StoreSuccessAsync(key, data, ttl);

        // Act
        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(key, result.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Success, result.Status);
        Assert.Equal(data, result.SuccessResult);
        Assert.Null(result.FailureType);
        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public async Task GetAsync_WithExistingFailureKey_ReturnsResponse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromMinutes(30);

        await store.StoreFailureAsync(key, exception, ttl);

        // Act
        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(key, result.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.FailureType);
        Assert.Equal("Test error", result.FailureMessage);
        Assert.Null(result.SuccessResult);
    }

    [Fact]
    public async Task GetAsync_WithExpiredKey_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromHours(1);

        await store.StoreSuccessAsync(key, "data", ttl);

        // Advance time beyond TTL
        timeProvider.Advance(TimeSpan.FromHours(2));

        // Act
        var result = await store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithExpiredKey_RemovesFromCache()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromMinutes(30);

        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var result1 = await store.GetAsync(key);
        var exists = await store.ExistsAsync(key);

        // Assert
        Assert.Null(result1);
        Assert.False(exists);
    }

    [Fact]
    public async Task GetAsync_WithKeyAtExactExpiryTime_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromHours(1);

        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(ttl); // Exactly at expiry

        // Act
        var result = await store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithKeyJustBeforeExpiry_ReturnsResponse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromHours(1);

        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(ttl - TimeSpan.FromMilliseconds(1));

        // Act
        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(key, result.IdempotencyKey);
    }

    #endregion

    #region GetAsync - Error Scenarios

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetAsync(null!).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(string.Empty).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    #endregion

    #region StoreSuccessAsync - Success Scenarios

    [Fact]
    public async Task StoreSuccessAsync_WithValidParameters_StoresResponse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var result = new { Data = "test-data" };
        var ttl = TimeSpan.FromHours(24);

        // Act
        await store.StoreSuccessAsync(key, result, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(key, retrieved.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.Equal(result, retrieved.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithNullResult_StoresSuccessfully()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromHours(1);

        // Act
        await store.StoreSuccessAsync(key, null, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.Null(retrieved.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccessAsync_SetsCorrectTimestamps()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromHours(2);
        var expectedStoredAt = timeProvider.GetUtcNow();
        var expectedExpiresAt = expectedStoredAt.Add(ttl);

        // Act
        await store.StoreSuccessAsync(key, "data", ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(expectedStoredAt.UtcDateTime, retrieved.StoredAt.UtcDateTime);
        Assert.Equal(expectedExpiresAt.UtcDateTime, retrieved.ExpiresAt.UtcDateTime);
    }

    [Fact]
    public async Task StoreSuccessAsync_OverwritesPreviousValue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var firstData = "first-data";
        var secondData = "second-data";
        var ttl = TimeSpan.FromHours(1);

        // Act
        await store.StoreSuccessAsync(key, firstData, ttl);
        await store.StoreSuccessAsync(key, secondData, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(secondData, retrieved.SuccessResult);
    }

    #endregion

    #region StoreSuccessAsync - Error Scenarios

    [Fact]
    public async Task StoreSuccessAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreSuccessAsync(null!, "data", TimeSpan.FromHours(1)).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreSuccessAsync(string.Empty, "data", TimeSpan.FromHours(1)).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    #endregion

    #region StoreFailureAsync - Success Scenarios

    [Fact]
    public async Task StoreFailureAsync_WithValidParameters_StoresFailure()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var exception = new ArgumentException("Invalid argument");
        var ttl = TimeSpan.FromMinutes(30);

        // Act
        await store.StoreFailureAsync(key, exception, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(key, retrieved.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Failure, retrieved.Status);
        Assert.Equal(typeof(ArgumentException).FullName, retrieved.FailureType);
        Assert.Equal("Invalid argument", retrieved.FailureMessage);
        Assert.NotNull(retrieved.FailureStackTrace);
    }

    [Fact]
    public async Task StoreFailureAsync_WithExceptionWithoutStackTrace_StoresNullStackTrace()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var exception = new Exception("Test exception"); // No stack trace
        var ttl = TimeSpan.FromMinutes(30);

        // Act
        await store.StoreFailureAsync(key, exception, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.FailureStackTrace);
    }

    [Fact]
    public async Task StoreFailureAsync_SetsCorrectTimestamps()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var exception = new Exception("Test");
        var ttl = TimeSpan.FromMinutes(45);
        var expectedStoredAt = timeProvider.GetUtcNow();
        var expectedExpiresAt = expectedStoredAt.Add(ttl);

        // Act
        await store.StoreFailureAsync(key, exception, ttl);
        var retrieved = await store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(expectedStoredAt.UtcDateTime, retrieved.StoredAt.UtcDateTime);
        Assert.Equal(expectedExpiresAt.UtcDateTime, retrieved.ExpiresAt.UtcDateTime);
    }

    #endregion

    #region StoreFailureAsync - Error Scenarios

    [Fact]
    public async Task StoreFailureAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreFailureAsync(null!, new Exception(), TimeSpan.FromHours(1)).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task StoreFailureAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreFailureAsync(string.Empty, new Exception(), TimeSpan.FromHours(1)).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task StoreFailureAsync_WithNullException_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreFailureAsync("key", null!, TimeSpan.FromHours(1)).AsTask());
        Assert.Equal("exception", exception.ParamName);
    }

    #endregion

    #region ExistsAsync - Success Scenarios

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act
        var exists = await store.ExistsAsync("non-existent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";

        await store.StoreSuccessAsync(key, "data", TimeSpan.FromHours(1));

        // Act
        var exists = await store.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";
        var ttl = TimeSpan.FromMinutes(30);

        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var exists = await store.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_RemovesFromCache()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";

        await store.StoreSuccessAsync(key, "data", TimeSpan.FromMinutes(10));
        timeProvider.Advance(TimeSpan.FromMinutes(15));

        // Act
        var exists1 = await store.ExistsAsync(key);
        var exists2 = await store.ExistsAsync(key);

        // Assert
        Assert.False(exists1);
        Assert.False(exists2);
    }

    #endregion

    #region ExistsAsync - Error Scenarios

    [Fact]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => store.ExistsAsync(null!).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => store.ExistsAsync(string.Empty).AsTask());
        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    #endregion

    #region CleanupExpiredAsync - Success Scenarios

    [Fact]
    public async Task CleanupExpiredAsync_WithNoEntries_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        // Act
        var count = await store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithNoExpiredEntries_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        await store.StoreSuccessAsync("key1", "data1", TimeSpan.FromHours(1));
        await store.StoreSuccessAsync("key2", "data2", TimeSpan.FromHours(2));

        // Act
        var count = await store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithExpiredEntries_RemovesThem()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        await store.StoreSuccessAsync("key1", "data1", TimeSpan.FromMinutes(30));
        await store.StoreSuccessAsync("key2", "data2", TimeSpan.FromMinutes(45));

        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var count = await store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(2, count);
        Assert.False(await store.ExistsAsync("key1"));
        Assert.False(await store.ExistsAsync("key2"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithMixedEntries_RemovesOnlyExpired()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        await store.StoreSuccessAsync("expired1", "data1", TimeSpan.FromMinutes(30));
        await store.StoreSuccessAsync("expired2", "data2", TimeSpan.FromMinutes(45));
        await store.StoreSuccessAsync("valid1", "data3", TimeSpan.FromHours(2));
        await store.StoreSuccessAsync("valid2", "data4", TimeSpan.FromHours(3));

        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var count = await store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(2, count);
        Assert.False(await store.ExistsAsync("expired1"));
        Assert.False(await store.ExistsAsync("expired2"));
        Assert.True(await store.ExistsAsync("valid1"));
        Assert.True(await store.ExistsAsync("valid2"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithEntryAtExactExpiry_RemovesIt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var ttl = TimeSpan.FromMinutes(30);

        await store.StoreSuccessAsync("key1", "data1", ttl);
        timeProvider.Advance(ttl); // Exactly at expiry

        // Act
        var count = await store.CleanupExpiredAsync();

        // Assert
        Assert.Equal(1, count);
        Assert.False(await store.ExistsAsync("key1"));
    }

    [Fact]
    public async Task CleanupExpiredAsync_MultipleCleanups_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        await store.StoreSuccessAsync("key1", "data1", TimeSpan.FromMinutes(10));
        await store.StoreSuccessAsync("key2", "data2", TimeSpan.FromMinutes(20));
        await store.StoreSuccessAsync("key3", "data3", TimeSpan.FromMinutes(30));

        // Act & Assert - First cleanup
        timeProvider.Advance(TimeSpan.FromMinutes(15));
        var count1 = await store.CleanupExpiredAsync();
        Assert.Equal(1, count1);

        // Act & Assert - Second cleanup
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var count2 = await store.CleanupExpiredAsync();
        Assert.Equal(1, count2);

        // Act & Assert - Third cleanup
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var count3 = await store.CleanupExpiredAsync();
        Assert.Equal(1, count3);

        // Verify all removed
        Assert.False(await store.ExistsAsync("key1"));
        Assert.False(await store.ExistsAsync("key2"));
        Assert.False(await store.ExistsAsync("key3"));
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentOperations_StoreAndGet_WorkCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var tasks = new List<Task>();

        // Act - Store 100 items concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.StoreSuccessAsync($"key-{index}", $"data-{index}", TimeSpan.FromHours(1));
            }));
        }

        await Task.WhenAll(tasks);
        tasks.Clear();

        // Act - Get 100 items concurrently
        var results = new IdempotencyResponse?[100];
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                results[index] = await store.GetAsync($"key-{index}");
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < 100; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal($"key-{i}", results[i]!.IdempotencyKey);
            Assert.Equal($"data-{i}", results[i]!.SuccessResult);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task MultipleStores_SameKey_LastWriteWins()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "duplicate-key";

        // Act
        await store.StoreSuccessAsync(key, "first", TimeSpan.FromHours(1));
        await store.StoreSuccessAsync(key, "second", TimeSpan.FromHours(1));
        await store.StoreSuccessAsync(key, "third", TimeSpan.FromHours(1));

        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("third", result.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccess_ThenStoreFailure_SameKey_OverwritesWithFailure()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "test-key";

        // Act
        await store.StoreSuccessAsync(key, "success-data", TimeSpan.FromHours(1));
        await store.StoreFailureAsync(key, new Exception("Error"), TimeSpan.FromMinutes(30));

        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Failure, result.Status);
        Assert.Null(result.SuccessResult);
    }

    [Fact]
    public async Task VeryShortTtl_ExpiresQuickly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "short-lived";
        var ttl = TimeSpan.FromMilliseconds(100);

        // Act
        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(TimeSpan.FromMilliseconds(101));

        var result = await store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task VeryLongTtl_RemainsValid()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        var key = "long-lived";
        var ttl = TimeSpan.FromDays(365);

        // Act
        await store.StoreSuccessAsync(key, "data", ttl);
        timeProvider.Advance(TimeSpan.FromDays(364));

        var result = await store.GetAsync(key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("data", result.SuccessResult);
    }

    #endregion
}
