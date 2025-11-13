<<<<<<< HEAD
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
=======
using Microsoft.Data.SqlClient;
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
>>>>>>> testing/storage
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

<<<<<<< HEAD
public class SqlServerOutboxStorageTests
{
    private readonly Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class SqlServerOutboxStorageTests : IDisposable
{
    private readonly Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonOptionsProvider> _mockJsonOptionsProvider;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly Mock<SqlConnection> _mockConnection;
>>>>>>> testing/storage
    private readonly SqlServerStorageOptions _options;

    public SqlServerOutboxStorageTests()
    {
        _mockConnectionProvider = new Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>>();
        _mockSchemaInitializer = new Mock<IDbSchemaInitializer>();
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
            OutboxTableName = "outbox_messages",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(_options, _timeProvider, null!));

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
            OutboxTableName = "outbox_messages",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new SqlServerOutboxStorage(
            _options,
            _timeProvider,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object);

        // Assert
=======
        _mockJsonOptionsProvider = new Mock<IJsonOptionsProvider>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _mockConnection = new Mock<SqlConnection>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            OutboxTableName = "outbox",
            Schema = "dbo"
        };

        _mockConnectionProvider
            .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConnection.Object);

        _mockConnectionProvider
            .Setup(x => x.IsSharedConnection)
            .Returns(false);

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonOptionsProvider
            .Setup(x => x.GetOptions())
            .Returns(new JsonSerializerOptions());
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        var storage = new SqlServerOutboxStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
>>>>>>> testing/storage
        Assert.NotNull(storage);
    }

    [Fact]
<<<<<<< HEAD
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithSchemaAndTableName_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            OutboxTableName = "custom_outbox"
        };

        // Act
        var fullName = options.GetFullTableName(options.OutboxTableName);

        // Assert
        Assert.Equal("[custom_schema].[custom_outbox]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            OutboxTableName = "outbox_messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.OutboxTableName);

        // Assert
        Assert.Equal("[dbo].[outbox_messages]", fullName);
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
=======
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(
                (SqlServerStorageOptions)null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public async Task AddAsync_WithValidMessage_ReturnsOutboxEntry()
    {
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new OutboxOptions { Destination = "queue1", MaxRetries = 3 };

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(message, It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        var result = await storage.AddAsync(message, options);
        Assert.NotNull(result);
        Assert.Equal(OutboxStatus.Pending, result.Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithValidQuery_ReturnsOutboxEntries()
    {
        var storage = CreateStorage();
        var query = new OutboxQuery { Status = OutboxEntryStatus.Pending, Limit = 100 };
        var result = await storage.GetPendingAsync(query);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithValidEntryId_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.MarkProcessedAsync(entryId);
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithValidEntryIdAndCount_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.UpdateRetryCountAsync(entryId, 2);
        Assert.False(result);
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCount()
    {
        var storage = CreateStorage();
        var result = await storage.GetPendingCountAsync();
        Assert.IsType<long>(result);
    }

    [Fact]
    public async Task GetFailedAsync_ReturnsOutboxEntries()
    {
        var storage = CreateStorage();
        var result = await storage.GetFailedAsync();
        Assert.NotNull(result);
    }

    private SqlServerOutboxStorage CreateStorage()
    {
        return new SqlServerOutboxStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
    }

    private static IMessage CreateTestMessage()
    {
        var mockMessage = new Mock<IMessage>();
        mockMessage.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        mockMessage.Setup(x => x.Timestamp).Returns(DateTimeOffset.UtcNow);
        mockMessage.Setup(x => x.CorrelationId).Returns(Guid.NewGuid().ToString());
        return mockMessage.Object;
    }

    public void Dispose()
    {
        // Mock objects don't need disposal
>>>>>>> testing/storage
    }
}
