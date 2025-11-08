using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests;

[Trait("Category", "Unit")]
public sealed class SqlServerIdempotencyStoreTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly DateTime _now;
    private readonly IJsonSerializer _jsonSerializer;

    public SqlServerIdempotencyStoreTests()
    {
        _timeProvider = new FakeTimeProvider();
        _now = _timeProvider.GetUtcNow().UtcDateTime;
        _jsonSerializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
    }

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Arrange & Act
        var store = new SqlServerIdempotencyStore(
            "Server=localhost;Database=test;",
            _timeProvider,
            _jsonSerializer);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerIdempotencyStore(null!, _timeProvider, _jsonSerializer));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new SqlServerIdempotencyStore(string.Empty, _timeProvider, _jsonSerializer));

        Assert.Equal("connectionString", exception.ParamName);
        Assert.Contains("cannot be empty or whitespace", exception.Message);
    }

    [Fact]
    public void Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new SqlServerIdempotencyStore("   ", _timeProvider, _jsonSerializer));

        Assert.Equal("connectionString", exception.ParamName);
        Assert.Contains("cannot be empty or whitespace", exception.Message);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerIdempotencyStore("Server=localhost;", null!, _jsonSerializer));

        Assert.Equal("timeProvider", exception.ParamName);
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.GetAsync(null!).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetAsync(string.Empty).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreSuccessAsync(null!, "result", TimeSpan.FromHours(1)).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task StoreSuccessAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreSuccessAsync(string.Empty, "result", TimeSpan.FromHours(1)).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public async Task StoreFailureAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);
        var exception = new InvalidOperationException("test error");

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreFailureAsync(null!, exception, TimeSpan.FromHours(1)).AsTask());

        Assert.Equal("idempotencyKey", thrownException.ParamName);
    }

    [Fact]
    public async Task StoreFailureAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);
        var exception = new InvalidOperationException("test error");

        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.StoreFailureAsync(string.Empty, exception, TimeSpan.FromHours(1)).AsTask());

        Assert.Equal("idempotencyKey", thrownException.ParamName);
        Assert.Contains("cannot be empty", thrownException.Message);
    }

    [Fact]
    public async Task StoreFailureAsync_WithNullException_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.StoreFailureAsync("test-key", null!, TimeSpan.FromHours(1)).AsTask());

        Assert.Equal("exception", exception.ParamName);
    }

    [Fact]
    public async Task ExistsAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.ExistsAsync(null!).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = new SqlServerIdempotencyStore("Server=localhost;Database=test;", _timeProvider, _jsonSerializer);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ExistsAsync(string.Empty).AsTask());

        Assert.Equal("idempotencyKey", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }
}
