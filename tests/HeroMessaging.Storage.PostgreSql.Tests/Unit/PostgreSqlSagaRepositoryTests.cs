using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

public class PostgreSqlSagaRepositoryTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlSagaRepositoryTests()
    {
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
            SagasTableName = "sagas",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "",
            Schema = "public",
            SagasTableName = "sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "   ",
            Schema = "public",
            SagasTableName = "sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidSchemaName_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            Schema = "invalid-schema",  // Contains invalid character
            SagasTableName = "sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithInvalidTableName_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            Schema = "public",
            SagasTableName = "invalid-table",  // Contains invalid character
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var repository = new PostgreSqlSagaRepository<TestSaga>(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(repository);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "public",
            SagasTableName = "sagas"
        };

        // Act
        var fullName = options.GetFullTableName(options.SagasTableName);

        // Assert
        Assert.Equal("public.sagas", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            SagasTableName = "custom_sagas"
        };

        // Act
        var fullName = options.GetFullTableName(options.SagasTableName);

        // Assert
        Assert.Equal("custom_schema.custom_sagas", fullName);
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
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            Schema = invalidSchema,
            SagasTableName = "sagas",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));

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
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            Schema = "public",
            SagasTableName = invalidTable,
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            new PostgreSqlSagaRepository<TestSaga>(invalidOptions, _timeProvider, _mockJsonSerializer.Object));

        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private class TestSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = "Initial";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
    }
}
