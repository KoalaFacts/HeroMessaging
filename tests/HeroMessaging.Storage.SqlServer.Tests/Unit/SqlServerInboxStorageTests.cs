using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerInboxStorageTests
{
    private readonly Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SqlServerStorageOptions _options;

    public SqlServerInboxStorageTests()
    {
        _mockConnectionProvider = new Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>>();
        _mockSchemaInitializer = new Mock<IDbSchemaInitializer>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
            InboxTableName = "inbox_messages",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerInboxStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerInboxStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerInboxStorage(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = null!,
            Schema = "dbo",
            InboxTableName = "inbox_messages",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerInboxStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new SqlServerInboxStorage(
            _options,
            _timeProvider,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithSchemaAndTableName_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            InboxTableName = "custom_inbox"
        };

        // Act
        var fullName = options.GetFullTableName(options.InboxTableName);

        // Assert
        Assert.Equal("[custom_schema].[custom_inbox]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            InboxTableName = "inbox_messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.InboxTableName);

        // Assert
        Assert.Equal("[dbo].[inbox_messages]", fullName);
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
