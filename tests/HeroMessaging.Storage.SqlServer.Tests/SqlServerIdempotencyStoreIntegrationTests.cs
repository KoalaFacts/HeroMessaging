using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests;

/// <summary>
/// Integration tests for SqlServerIdempotencyStore using Testcontainers.
/// </summary>
/// <remarks>
/// These tests automatically spin up a SQL Server container and run all tests against it.
/// No manual database setup is required - Docker is the only prerequisite.
/// All tests in this class share a single SQL Server container for performance.
/// </remarks>
[Trait("Category", "Integration")]
[Trait("Database", "SqlServer")]
[Collection(nameof(SqlServerIdempotencyStoreCollection))]
public sealed class SqlServerIdempotencyStoreIntegrationTests : IAsyncDisposable
{
    private readonly SqlServerIdempotencyStoreFixture _fixture;
    private readonly SqlServerIdempotencyStore _store;
    private readonly FakeTimeProvider _timeProvider;
    private readonly List<string> _keysToCleanup = new();
    private readonly IJsonSerializer _jsonSerializer;

    public SqlServerIdempotencyStoreIntegrationTests(SqlServerIdempotencyStoreFixture fixture)
    {
        _fixture = fixture;
        _timeProvider = new FakeTimeProvider();
        _jsonSerializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
        _store = new SqlServerIdempotencyStore(_fixture.ConnectionString, _timeProvider, _jsonSerializer);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var key = $"test:nonexistent:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);

        // Act
        var result = await _store.GetAsync(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task StoreSuccessAsync_ThenGet_ReturnsStoredResponse()
    {
        // Arrange
        var key = $"test:success:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var successResult = new { Value = "test-result", Count = 42 };
        var ttl = TimeSpan.FromHours(1);

        // Act - Store
        await _store.StoreSuccessAsync(key, successResult, ttl);

        // Act - Get
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(key, retrieved.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.NotNull(retrieved.SuccessResult);
        Assert.Null(retrieved.FailureType);
        Assert.Null(retrieved.FailureMessage);
        Assert.Null(retrieved.FailureStackTrace);
    }

    [Fact]
    public async Task StoreFailureAsync_ThenGet_ReturnsStoredFailure()
    {
        // Arrange
        var key = $"test:failure:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var exception = GetExceptionWithStackTrace();
        var ttl = TimeSpan.FromHours(1);

        // Act - Store
        await _store.StoreFailureAsync(key, exception, ttl);

        // Act - Get
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(key, retrieved.IdempotencyKey);
        Assert.Equal(IdempotencyStatus.Failure, retrieved.Status);
        Assert.Null(retrieved.SuccessResult);
        Assert.Equal("System.InvalidOperationException", retrieved.FailureType);
        Assert.Equal("Test error message", retrieved.FailureMessage);
        Assert.NotNull(retrieved.FailureStackTrace);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithNullResult_Succeeds()
    {
        // Arrange
        var key = $"test:null-result:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var ttl = TimeSpan.FromHours(1);

        // Act
        await _store.StoreSuccessAsync(key, null, ttl);
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.Null(retrieved.SuccessResult);
    }

    [Fact]
    public async Task StoreSuccessAsync_Twice_UpdatesExistingRecord()
    {
        // Arrange
        var key = $"test:update:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var firstResult = new { Value = "first" };
        var secondResult = new { Value = "second" };
        var ttl = TimeSpan.FromHours(1);

        // Act - Store first
        await _store.StoreSuccessAsync(key, firstResult, ttl);

        // Advance time
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act - Store second (should update)
        await _store.StoreSuccessAsync(key, secondResult, ttl);

        // Act - Retrieve
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.NotNull(retrieved.SuccessResult);
    }

    [Fact]
    public async Task GetAsync_WithExpiredEntry_ReturnsNull()
    {
        // Arrange
        var key = $"test:expired:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var result = "test-result";
        var ttl = TimeSpan.FromHours(1);

        // Act - Store with 1 hour TTL
        await _store.StoreSuccessAsync(key, result, ttl);

        // Act - Advance time beyond TTL
        _timeProvider.Advance(TimeSpan.FromHours(2));

        // Act - Try to retrieve
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var key = $"test:exists:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var ttl = TimeSpan.FromHours(1);

        // Act - Store
        await _store.StoreSuccessAsync(key, "test", ttl);

        // Act - Check existence
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var key = $"test:not-exists:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);

        // Act
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_ReturnsFalse()
    {
        // Arrange
        var key = $"test:expired-exists:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var ttl = TimeSpan.FromHours(1);

        // Act - Store
        await _store.StoreSuccessAsync(key, "test", ttl);

        // Act - Advance time beyond TTL
        _timeProvider.Advance(TimeSpan.FromHours(2));

        // Act - Check existence
        var exists = await _store.ExistsAsync(key);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesExpiredEntries()
    {
        // Arrange
        var key1 = $"test:cleanup1:{Guid.NewGuid()}";
        var key2 = $"test:cleanup2:{Guid.NewGuid()}";
        _keysToCleanup.Add(key1);
        _keysToCleanup.Add(key2);

        // Store two entries with different TTLs
        await _store.StoreSuccessAsync(key1, "test1", TimeSpan.FromMinutes(30));
        await _store.StoreSuccessAsync(key2, "test2", TimeSpan.FromHours(2));

        // Advance time to expire first entry
        _timeProvider.Advance(TimeSpan.FromHours(1));

        // Act - Cleanup
        var removedCount = await _store.CleanupExpiredAsync();

        // Assert
        Assert.True(removedCount >= 1); // At least key1 should be removed
        Assert.False(await _store.ExistsAsync(key1)); // First entry should be gone
        Assert.True(await _store.ExistsAsync(key2)); // Second entry should remain
    }

    [Fact]
    public async Task StoreSuccessAsync_WithComplexObject_RoundTripsCorrectly()
    {
        // Arrange
        var key = $"test:complex:{Guid.NewGuid()}";
        _keysToCleanup.Add(key);
        var complexResult = new
        {
            Id = Guid.NewGuid(),
            Name = "Test Object",
            Count = 123,
            Values = new[] { 1, 2, 3, 4, 5 },
            Nested = new
            {
                Property1 = "nested value",
                Property2 = 456
            }
        };
        var ttl = TimeSpan.FromHours(1);

        // Act
        await _store.StoreSuccessAsync(key, complexResult, ttl);
        var retrieved = await _store.GetAsync(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(IdempotencyStatus.Success, retrieved.Status);
        Assert.NotNull(retrieved.SuccessResult);
    }

    public async ValueTask DisposeAsync()
    {
        // Cleanup test data (best effort) - container is managed by the fixture
        if (_keysToCleanup.Count > 0)
        {
            try
            {
                await using var connection = new SqlConnection(_fixture.ConnectionString);
                await connection.OpenAsync();

                foreach (var key in _keysToCleanup)
                {
                    try
                    {
                        await using var command = connection.CreateCommand();
                        command.CommandText = "DELETE FROM IdempotencyResponses WHERE IdempotencyKey = @Key";
                        command.Parameters.AddWithValue("@Key", key);
                        await command.ExecuteNonQueryAsync();
                    }
                    catch
                    {
                        // Ignore individual cleanup failures
                    }
                }
            }
            catch
            {
                // Ignore connection failures during cleanup
            }
        }
    }

    private static Exception GetExceptionWithStackTrace()
    {
        try
        {
            throw new InvalidOperationException("Test error message");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
