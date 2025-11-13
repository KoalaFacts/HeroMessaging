using System.Data.SqlClient;
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerOutboxStorageTests : IDisposable
{
    private readonly Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonOptionsProvider> _mockJsonOptionsProvider;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly Mock<SqlConnection> _mockConnection;
    private readonly SqlServerStorageOptions _options;

    public SqlServerOutboxStorageTests()
    {
        _mockConnectionProvider = new Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>>();
        _mockSchemaInitializer = new Mock<IDbSchemaInitializer>();
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
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object,
            _mockJsonOptionsProvider.Object);
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerOutboxStorage(
                null!,
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
        mockMessage.Setup(x => x.CorrelationId).Returns(Guid.NewGuid());
        return mockMessage.Object;
    }

    public void Dispose()
    {
        _mockConnectionProvider?.Dispose();
        _mockSchemaInitializer?.Dispose();
        _mockJsonOptionsProvider?.Dispose();
        _mockTimeProvider?.Dispose();
        _mockJsonSerializer?.Dispose();
        _mockConnection?.Dispose();
    }
}
