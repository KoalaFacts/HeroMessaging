using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

public class PostgreSqlMessageStorageTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlMessageStorageTests()
    {
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
            MessagesTableName = "messages",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var invalidOptions = new PostgreSqlStorageOptions
        {
            ConnectionString = null!,
            Schema = "public",
            MessagesTableName = "messages",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new PostgreSqlMessageStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnection_CreatesInstance()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");
        var mockTransaction = new Mock<NpgsqlTransaction>();

        // Act
        var storage = new PostgreSqlMessageStorage(
            mockConnection.Object,
            mockTransaction.Object,
            _timeProvider,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnectionNullConnection_ThrowsArgumentNullException()
    {
        // Arrange
        NpgsqlConnection? nullConnection = null;
        NpgsqlTransaction? transaction = null;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(nullConnection!, transaction, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "public",
            MessagesTableName = "messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("public.messages", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            MessagesTableName = "custom_messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("custom_schema.custom_messages", fullName);
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
