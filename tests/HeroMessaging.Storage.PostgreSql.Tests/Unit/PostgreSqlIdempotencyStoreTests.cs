using System.Text.Json;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PostgreSqlIdempotencyStoreTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly string _connectionString = "Host=localhost;Database=test";

    public PostgreSqlIdempotencyStoreTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(It.IsAny<object>(), It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        _mockJsonSerializer
            .Setup(x => x.DeserializeFromString<object>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(new object());
    }

    [Fact]
    public void Constructor_WithValidConnectionString_Succeeds()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test";

        // Act
        var store = new PostgreSqlIdempotencyStore(
            connectionString,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        string? nullConnectionString = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlIdempotencyStore(
                nullConnectionString!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var emptyConnectionString = string.Empty;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new PostgreSqlIdempotencyStore(
                emptyConnectionString,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var whitespaceConnectionString = "   ";

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new PostgreSqlIdempotencyStore(
                whitespaceConnectionString,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        TimeProvider? nullTimeProvider = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlIdempotencyStore(
                _connectionString,
                nullTimeProvider!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange
        IJsonSerializer? nullSerializer = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlIdempotencyStore(
                _connectionString,
                _mockTimeProvider.Object,
                nullSerializer!));
    }

    [Fact]
    public async ValueTask GetAsync_WithValidKey_ReturnsNull()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";

        // Act
        var result = await store.GetAsync(idempotencyKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result); // Mocked connection returns null
    }

    [Fact]
    public async ValueTask GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = CreateStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.GetAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = CreateStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.GetAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithValidKeyAndResult_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var result = new { Message = "Success" };
        var ttl = TimeSpan.FromHours(24);

        // Act
        await store.StoreSuccessAsync(idempotencyKey, result, ttl, TestContext.Current.CancellationToken);

        // Assert - no exception thrown
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = CreateStore();
        var ttl = TimeSpan.FromHours(24);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreSuccessAsync(null!, new object(), ttl, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = CreateStore();
        var ttl = TimeSpan.FromHours(24);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.StoreSuccessAsync(string.Empty, new object(), ttl, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithNullResult_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.FromHours(24);

        // Act
        await store.StoreSuccessAsync(idempotencyKey, null, ttl, TestContext.Current.CancellationToken);

        // Assert - no exception thrown
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithZeroTtl_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.Zero;

        // Act
        await store.StoreSuccessAsync(idempotencyKey, new object(), ttl, TestContext.Current.CancellationToken);

        // Assert - no exception thrown
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithValidKeyAndException_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromHours(1);

        // Act
        await store.StoreFailureAsync(idempotencyKey, exception, ttl, TestContext.Current.CancellationToken);

        // Assert - no exception thrown
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = CreateStore();
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromHours(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreFailureAsync(null!, exception, ttl, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = CreateStore();
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromHours(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.StoreFailureAsync(string.Empty, exception, ttl, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithNullException_ThrowsArgumentNullException()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.FromHours(1);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreFailureAsync(idempotencyKey, null!, ttl, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask ExistsAsync_WithValidKey_ReturnsFalse()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";

        // Act
        var result = await store.ExistsAsync(idempotencyKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result); // Mocked connection returns false
    }

    [Fact]
    public async ValueTask ExistsAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var store = CreateStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.ExistsAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask ExistsAsync_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var store = CreateStore();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.ExistsAsync(string.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask CleanupExpiredAsync_ReturnsCount()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var result = await store.CleanupExpiredAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<int>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async ValueTask CleanupExpiredAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var store = CreateStore();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.CleanupExpiredAsync(cts.Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithLongTtl_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.FromDays(365);

        // Act
        await store.StoreSuccessAsync(idempotencyKey, new object(), ttl, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithComplexException_Succeeds()
    {
        // Arrange
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var innerException = new ArgumentNullException("inner");
        var exception = new InvalidOperationException("outer", innerException);
        var ttl = TimeSpan.FromHours(1);

        // Act
        await store.StoreFailureAsync(idempotencyKey, exception, ttl, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(store);
    }

    private PostgreSqlIdempotencyStore CreateStore()
    {
        return new PostgreSqlIdempotencyStore(
            _connectionString,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
    }

    public void Dispose()
    {
        // Mock objects don't need disposal
    }
}
