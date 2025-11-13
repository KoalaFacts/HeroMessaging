using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerDeadLetterQueueTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SqlServerStorageOptions _options;

    public SqlServerDeadLetterQueueTests()
    {
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
            DeadLetterTableName = "DeadLetterQueue",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var dlq = new SqlServerDeadLetterQueue(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(dlq);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            DeadLetterTableName = "DeadLetterQueue"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("[dbo].[DeadLetterQueue]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            DeadLetterTableName = "CustomDLQ"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("[custom_schema].[CustomDLQ]", fullName);
    }

    [Theory]
    [InlineData("test_schema", "dlq_table", "[test_schema].[dlq_table]")]
    [InlineData("dbo", "DeadLetters", "[dbo].[DeadLetters]")]
    [InlineData("production", "DLQ", "[production].[DLQ]")]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithVariousSchemaAndTableNames_ReturnsCorrectQualifiedName(
        string schema, string tableName, string expected)
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = schema,
            DeadLetterTableName = tableName
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal(expected, fullName);
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string Content { get; set; } = "Test content";
    }
}
