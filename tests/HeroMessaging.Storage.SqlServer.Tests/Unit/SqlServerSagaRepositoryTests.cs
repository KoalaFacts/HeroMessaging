<<<<<<< HEAD
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
=======
using System.Text.Json;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Utilities;
>>>>>>> testing/storage
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

<<<<<<< HEAD
public class SqlServerSagaRepositoryTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class SqlServerSagaRepositoryTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
>>>>>>> testing/storage
    private readonly SqlServerStorageOptions _options;

    public SqlServerSagaRepositoryTests()
    {
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
            SagasTableName = "Sagas",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSagaRepository<TestSaga>(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSagaRepository<TestSaga>(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSagaRepository<TestSaga>(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "",
            Schema = "dbo",
            SagasTableName = "Sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "   ",
            Schema = "dbo",
            SagasTableName = "Sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidSchemaName_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            Schema = "invalid-schema",  // Contains invalid character
            SagasTableName = "Sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidTableName_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            Schema = "dbo",
            SagasTableName = "invalid-table",  // Contains invalid character
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var repository = new SqlServerSagaRepository<TestSaga>(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
=======
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
>>>>>>> testing/storage
        Assert.NotNull(repository);
    }

    [Fact]
<<<<<<< HEAD
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            SagasTableName = "Sagas"
        };

        // Act
        var fullName = options.GetFullTableName(options.SagasTableName);

        // Assert
        Assert.Equal("[dbo].[Sagas]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            SagasTableName = "CustomSagas"
        };

        // Act
        var fullName = options.GetFullTableName(options.SagasTableName);

        // Assert
        Assert.Equal("[custom_schema].[CustomSagas]", fullName);
    }

    [Theory]
    [InlineData("", "Schema cannot be empty")]
    [InlineData("   ", "Schema cannot be empty")]
    [InlineData("1invalid", "Invalid identifier")]
    [InlineData("invalid-name", "Invalid identifier")]
    [InlineData("invalid.name", "Invalid identifier")]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidSchemaNames_ThrowsArgumentException(string invalidSchema, string expectedMessagePart)
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            Schema = invalidSchema,
            SagasTableName = "Sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));

        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "Identifier cannot be empty")]
    [InlineData("   ", "Identifier cannot be empty")]
    [InlineData("1invalid", "Invalid identifier")]
    [InlineData("invalid-name", "Invalid identifier")]
    [InlineData("invalid.name", "Invalid identifier")]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidTableNames_ThrowsArgumentException(string invalidTable, string expectedMessagePart)
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            Schema = "dbo",
            SagasTableName = invalidTable,
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new SqlServerSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));

        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
=======
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

        var result = await repository.FindAsync(correlationId);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByStateAsync_WithValidState_ReturnsEmptyCollection()
    {
        var repository = CreateRepository();
        var state = "InitialState";

        var result = await repository.FindByStateAsync(state);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindByStateAsync_WithNullState_ThrowsArgumentException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await repository.FindByStateAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_WithValidSaga_Succeeds()
    {
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid() };

        await repository.SaveAsync(saga);
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task SaveAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.SaveAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_WithValidSaga_Throws()
    {
        var repository = CreateRepository();
        var saga = new TestSaga { CorrelationId = Guid.NewGuid(), Version = 0 };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.UpdateAsync(saga));
    }

    [Fact]
    public async Task UpdateAsync_WithNullSaga_ThrowsArgumentNullException()
    {
        var repository = CreateRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await repository.UpdateAsync(null!));
    }

    [Fact]
    public async Task DeleteAsync_WithValidCorrelationId_Succeeds()
    {
        var repository = CreateRepository();
        var correlationId = Guid.NewGuid();

        await repository.DeleteAsync(correlationId);
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task FindStaleAsync_WithValidTimeSpan_ReturnsEmptyCollection()
    {
        var repository = CreateRepository();
        var olderThan = TimeSpan.FromHours(1);

        var result = await repository.FindStaleAsync(olderThan);
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
>>>>>>> testing/storage
    }

    private class TestSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
<<<<<<< HEAD
        public string CurrentState { get; set; } = "Initial";
=======
        public string CurrentState { get; set; } = "InitialState";
>>>>>>> testing/storage
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
    }
}
