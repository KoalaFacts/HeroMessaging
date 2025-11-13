using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

public class PostgreSqlDeadLetterQueueTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlDeadLetterQueueTests()
    {
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
            DeadLetterTableName = "dead_letter_queue",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlDeadLetterQueue(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlDeadLetterQueue(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var dlq = new PostgreSqlDeadLetterQueue(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(dlq);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "public",
            DeadLetterTableName = "dead_letter_queue"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("public.dead_letter_queue", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            DeadLetterTableName = "custom_dlq"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("custom_schema.custom_dlq", fullName);
    }

    [Theory]
    [InlineData("test_schema", "dlq_table", "test_schema.dlq_table")]
    [InlineData("public", "dead_letters", "public.dead_letters")]
    [InlineData("production", "dlq", "production.dlq")]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithVariousSchemaAndTableNames_ReturnsCorrectQualifiedName(
        string schema, string tableName, string expected)
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
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
