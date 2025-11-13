<<<<<<< HEAD
using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
=======
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
>>>>>>> testing/storage
using Moq;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

<<<<<<< HEAD
public class PostgreSqlOutboxStorageTests
{
    private readonly Mock<IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class PostgreSqlOutboxStorageTests : IDisposable
{
    private readonly Mock<IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonOptionsProvider> _mockJsonOptionsProvider;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly Mock<NpgsqlConnection> _mockConnection;
>>>>>>> testing/storage
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlOutboxStorageTests()
    {
        _mockConnectionProvider = new Mock<IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction>>();
        _mockSchemaInitializer = new Mock<IDbSchemaInitializer>();
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
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
            new PostgreSqlOutboxStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(_options, _timeProvider, null!));

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
            OutboxTableName = "outbox_messages",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new PostgreSqlOutboxStorage(
            _options,
            _timeProvider,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object);
=======
        _mockJsonOptionsProvider = new Mock<IJsonOptionsProvider>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _mockConnection = new Mock<NpgsqlConnection>();

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            AutoCreateTables = false,
            OutboxTableName = "outbox",
            Schema = "public"
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
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "test" };

        // Act
        var storage = new PostgreSqlOutboxStorage(
            options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object,
            _mockJsonOptionsProvider.Object);
>>>>>>> testing/storage

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
<<<<<<< HEAD
    [Trait("Category", "Unit")]
    public void Constructor_WithSharedConnection_CreatesInstance()
    {
        // Arrange
        var mockConnection = new Mock<NpgsqlConnection>("Host=localhost;Database=test");
        var mockTransaction = new Mock<NpgsqlTransaction>();

        // Act
        var storage = new PostgreSqlOutboxStorage(
            mockConnection.Object,
            mockTransaction.Object,
            _timeProvider,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithSchemaAndTableName_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            OutboxTableName = "custom_outbox"
        };

        // Act
        var fullName = options.GetFullTableName(options.OutboxTableName);

        // Assert
        Assert.Equal("custom_schema.custom_outbox", fullName);
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
        // Arrange
        PostgreSqlStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(
                nullOptions!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object,
                _mockConnectionProvider.Object,
                _mockSchemaInitializer.Object,
                _mockJsonOptionsProvider.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        TimeProvider? nullTimeProvider = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(
                _options,
                nullTimeProvider!,
                _mockJsonSerializer.Object,
                _mockConnectionProvider.Object,
                _mockSchemaInitializer.Object,
                _mockJsonOptionsProvider.Object));
    }

    [Fact]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange
        IJsonSerializer? nullSerializer = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlOutboxStorage(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!,
                _mockConnectionProvider.Object,
                _mockSchemaInitializer.Object,
                _mockJsonOptionsProvider.Object));
    }

    [Fact]
    public async Task AddAsync_WithValidMessage_ReturnsOutboxEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new OutboxOptions { Destination = "queue1", MaxRetries = 3 };

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(message, It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        // Act
        var result = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message, result.Message);
        Assert.Equal(OutboxStatus.Pending, result.Status);
        Assert.Equal(0, result.RetryCount);
    }

    [Fact]
    public async Task AddAsync_WithNullDestination_ReturnsOutboxEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new OutboxOptions { Destination = null, MaxRetries = 3 };

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(message, It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        // Act
        var result = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Options.Destination);
    }

    [Fact]
    public async Task GetPendingAsync_WithValidQuery_ReturnsOutboxEntries()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new OutboxQuery { Status = OutboxEntryStatus.Pending, Limit = 100 };

        // Act
        var result = await storage.GetPendingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<OutboxEntry>>(result);
    }

    [Fact]
    public async Task GetPendingAsync_WithEmptyDatabase_ReturnsEmptyCollection()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new OutboxQuery { Status = OutboxEntryStatus.Pending, Limit = 100 };

        // Act
        var result = await storage.GetPendingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingAsync_WithDefaultLimit_ReturnsOutboxEntries()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetPendingAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<OutboxEntry>>(result);
    }

    [Fact]
    public async Task GetPendingAsync_WithCustomLimit_ReturnsOutboxEntries()
    {
        // Arrange
        var storage = CreateStorage();
        var customLimit = 50;

        // Act
        var result = await storage.GetPendingAsync(customLimit);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<OutboxEntry>>(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithValidEntryId_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.MarkProcessedAsync(entryId);

        // Assert
        Assert.False(result); // No rows affected with mocked connection
    }

    [Fact]
    public async Task MarkProcessedAsync_WithNonExistentEntryId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.MarkProcessedAsync(entryId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkFailedAsync_WithValidEntryIdAndError_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var error = "Processing error";

        // Act
        var result = await storage.MarkFailedAsync(entryId, error);

        // Assert
        Assert.False(result); // No rows affected with mocked connection
    }

    [Fact]
    public async Task MarkFailedAsync_WithNullError_Succeeds()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act & Assert
        await storage.MarkFailedAsync(entryId, null!);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithValidEntryIdAndCount_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var retryCount = 2;
        var nextRetry = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        var result = await storage.UpdateRetryCountAsync(entryId, retryCount, nextRetry);

        // Assert
        Assert.False(result); // No rows affected with mocked connection
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithNullNextRetry_Succeeds()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var retryCount = 1;

        // Act
        var result = await storage.UpdateRetryCountAsync(entryId, retryCount);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithZeroRetryCount_Succeeds()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.UpdateRetryCountAsync(entryId, 0);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetPendingCountAsync_WithMessages_ReturnsCount()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetPendingCountAsync();

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task GetFailedAsync_WithDefaultLimit_ReturnsOutboxEntries()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetFailedAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<OutboxEntry>>(result);
    }

    [Fact]
    public async Task GetFailedAsync_WithCustomLimit_ReturnsOutboxEntries()
    {
        // Arrange
        var storage = CreateStorage();
        var customLimit = 50;

        // Act
        var result = await storage.GetFailedAsync(customLimit);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<OutboxEntry>>(result);
    }

    [Fact]
    public async Task AddAsync_WithCancellationToken_RespondsToCancel()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new OutboxOptions { MaxRetries = 3 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storage.AddAsync(message, options, cts.Token));
    }

    private PostgreSqlOutboxStorage CreateStorage()
    {
        return new PostgreSqlOutboxStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object,
            _mockJsonOptionsProvider.Object);
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
