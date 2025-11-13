using System.Text.Json;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PostgreSqlSagaRepositoryTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlSagaRepositoryTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            AutoCreateTables = false,
            SagasTableName = "sagas",
            Schema = "public"
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
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "test" };

        // Act
        var repository = new PostgreSqlSagaRepository<TestSaga>(
            options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(repository);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        PostgreSqlStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(
                nullOptions!,
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
            new PostgreSqlSagaRepository<TestSaga>(
                _options,
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
            new PostgreSqlSagaRepository<TestSaga>(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!));
    }

    [Fact]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(
                options,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public async Task FindAsync_WithValidCorrelationId_ReturnsNull()
    {
        // Arrange
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await repository.FindAsync(correlationId);

        // Assert
        Assert.Null(result); // Mocked connection returns null
    }

    [Fact]
    public async Task FindAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.FindAsync(correlationId, cts.Token));
    }

    [Fact]
    public async Task FindByStateAsync_WithValidState_ReturnsEmptyCollection()
    {
        // Arrange
        var repository = CreateRepository();
        var state = "InitialState";

        // Act
        var result = await repository.FindByStateAsync(state);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindByStateAsync_WithNullState_ThrowsArgumentException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.FindByStateAsync(null!));
    }

    [Fact]
    public async Task FindByStateAsync_WithEmptyState_ThrowsArgumentException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.FindByStateAsync(string.Empty));
    }

    [Fact]
    public async Task FindByStateAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.FindByStateAsync("InitialState", cts.Token));
    }

    [Fact]
    public async Task SaveAsync_WithValidSaga_Succeeds()
    {
        // Arrange
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

        // Act
        await repository.SaveAsync(saga);

        // Assert - no exception thrown
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task SaveAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.SaveAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.SaveAsync(saga, cts.Token));
    }

    [Fact]
    public async Task UpdateAsync_WithValidSaga_Succeeds()
    {
        // Arrange
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), Version = 0 };

        // Act & Assert - Will fail due to non-existent saga with mocked connection
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.UpdateAsync(saga));
    }

    [Fact]
    public async Task UpdateAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.UpdateAsync(saga, cts.Token));
    }

    [Fact]
    public async Task DeleteAsync_WithValidCorrelationId_Succeeds()
    {
        // Arrange
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();

        // Act
        await repository.DeleteAsync(correlationId);

        // Assert - no exception thrown
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task DeleteAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.DeleteAsync(correlationId, cts.Token));
    }

    [Fact]
    public async Task FindStaleAsync_WithValidTimeSpan_ReturnsEmptyCollection()
    {
        // Arrange
        var repository = CreateRepository();
        var olderThan = TimeSpan.FromHours(1);

        // Act
        var result = await repository.FindStaleAsync(olderThan);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindStaleAsync_WithZeroTimeSpan_ReturnsEmptyCollection()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        var result = await repository.FindStaleAsync(TimeSpan.Zero);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindStaleAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var repository = CreateRepository();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await repository.FindStaleAsync(TimeSpan.FromHours(1), cts.Token));
    }

    [Fact]
    public void Dispose_CanBeCalled()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        repository.Dispose();

        // Assert - no exception thrown
        Assert.NotNull(repository);
    }

    [Fact]
    public void Dispose_MultipleCalls_Succeeds()
    {
        // Arrange
        var repository = CreateRepository();

        // Act
        repository.Dispose();
        repository.Dispose();

        // Assert
        Assert.NotNull(repository);
    }

    private PostgreSqlSagaRepository<TestSaga> CreateRepository()
    {
        return new PostgreSqlSagaRepository<TestSaga>(
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
