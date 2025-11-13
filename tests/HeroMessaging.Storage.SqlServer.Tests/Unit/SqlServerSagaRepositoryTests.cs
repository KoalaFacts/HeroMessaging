using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerSagaRepositoryTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SqlServerStorageOptions _options;

    public SqlServerSagaRepositoryTests()
    {
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
        Assert.NotNull(repository);
    }

    [Fact]
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
