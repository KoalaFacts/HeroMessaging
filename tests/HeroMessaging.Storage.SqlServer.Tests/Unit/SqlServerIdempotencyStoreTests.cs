using System.Text.Json;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerIdempotencyStoreTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly string _connectionString = "Server=localhost;Database=test";

    public SqlServerIdempotencyStoreTests()
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
        var store = new SqlServerIdempotencyStore(
            _connectionString,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerIdempotencyStore(
                null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new SqlServerIdempotencyStore(
                string.Empty,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerIdempotencyStore(
                _connectionString,
                null!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public async ValueTask GetAsync_WithValidKey_ReturnsNull()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";

        var result = await store.GetAsync(idempotencyKey);
        Assert.Null(result);
    }

    [Fact]
    public async ValueTask GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.GetAsync(null!));
    }

    [Fact]
    public async ValueTask GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.GetAsync(string.Empty));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithValidKeyAndResult_Succeeds()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var result = new { Message = "Success" };
        var ttl = TimeSpan.FromHours(24);

        await store.StoreSuccessAsync(idempotencyKey, result, ttl);
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var store = CreateStore();
        var ttl = TimeSpan.FromHours(24);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreSuccessAsync(null!, new object(), ttl));
    }

    [Fact]
    public async ValueTask StoreSuccessAsync_WithNullResult_Succeeds()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.FromHours(24);

        await store.StoreSuccessAsync(idempotencyKey, null, ttl);
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithValidKeyAndException_Succeeds()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromHours(1);

        await store.StoreFailureAsync(idempotencyKey, exception, ttl);
        Assert.NotNull(store);
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var store = CreateStore();
        var exception = new InvalidOperationException("Test error");
        var ttl = TimeSpan.FromHours(1);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreFailureAsync(null!, exception, ttl));
    }

    [Fact]
    public async ValueTask StoreFailureAsync_WithNullException_ThrowsArgumentNullException()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";
        var ttl = TimeSpan.FromHours(1);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.StoreFailureAsync(idempotencyKey, null!, ttl));
    }

    [Fact]
    public async ValueTask ExistsAsync_WithValidKey_ReturnsFalse()
    {
        var store = CreateStore();
        var idempotencyKey = "idempotency:test-key";

        var result = await store.ExistsAsync(idempotencyKey);
        Assert.False(result);
    }

    [Fact]
    public async ValueTask ExistsAsync_WithNullKey_ThrowsArgumentNullException()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.ExistsAsync(null!));
    }

    [Fact]
    public async ValueTask CleanupExpiredAsync_ReturnsCount()
    {
        var store = CreateStore();

        var result = await store.CleanupExpiredAsync();
        Assert.IsType<int>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async ValueTask CleanupExpiredAsync_WithCancellation_RespondsToCancel()
    {
        var store = CreateStore();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.CleanupExpiredAsync(cts.Token));
    }

    private SqlServerIdempotencyStore CreateStore()
    {
        return new SqlServerIdempotencyStore(
            _connectionString,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
    }

    public void Dispose()
    {
        _mockTimeProvider?.Dispose();
        _mockJsonSerializer?.Dispose();
    }
}
