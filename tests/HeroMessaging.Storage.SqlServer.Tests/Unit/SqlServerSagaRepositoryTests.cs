using System.Text.Json;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerSagaRepositoryTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly SqlServerStorageOptions _options;

    public SqlServerSagaRepositoryTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            SagasTableName = "sagas",
            Schema = "dbo"
        };

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(It.IsAny<object>(), It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        _mockJsonSerializer
            .Setup(x => x.DeserializeFromString<TestSaga>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(new TestSaga());
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        var repository = new SqlServerSagaRepository<TestSaga>(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
        Assert.NotNull(repository);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSagaRepository<TestSaga>(
                null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSagaRepository<TestSaga>(
                _options,
                null!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        var options = new SqlServerStorageOptions { ConnectionString = "" };

        Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(
                options,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public async Task FindAsync_WithValidCorrelationId_ReturnsNull()
    {
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();

        var result = await repository.FindAsync(correlationId, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByStateAsync_WithValidState_ReturnsEmptyCollection()
    {
        var repository = CreateRepository();
        var state = "InitialState";

        var result = await repository.FindByStateAsync(state, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindByStateAsync_WithNullState_ThrowsArgumentException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.FindByStateAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_WithValidSaga_Succeeds()
    {
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

        await repository.SaveAsync(saga, TestContext.Current.CancellationToken);
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task SaveAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.SaveAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAsync_WithValidSaga_Throws()
    {
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), Version = 0 };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.UpdateAsync(saga, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.UpdateAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WithValidCorrelationId_Succeeds()
    {
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();

        await repository.DeleteAsync(correlationId, TestContext.Current.CancellationToken);
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task FindStaleAsync_WithValidTimeSpan_ReturnsEmptyCollection()
    {
        var repository = CreateRepository();
        var olderThan = TimeSpan.FromHours(1);

        var result = await repository.FindStaleAsync(olderThan, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Dispose_CanBeCalled()
    {
        var repository = CreateRepository();
        repository.Dispose();
        Assert.NotNull(repository);
    }

    private SqlServerSagaRepository<TestSaga> CreateRepository()
    {
        return new SqlServerSagaRepository<TestSaga>(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
    }

    public void Dispose()
    {
        // Mock objects don't need disposal
    }

    private class TestSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = "InitialState";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
    }
}
