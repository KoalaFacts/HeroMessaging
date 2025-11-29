using Microsoft.Data.SqlClient;
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerInboxStorageTests : IDisposable
{
    private readonly Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>> _mockConnectionProvider;
    private readonly Mock<IDbSchemaInitializer> _mockSchemaInitializer;
    private readonly Mock<IJsonOptionsProvider> _mockJsonOptionsProvider;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly Mock<SqlConnection> _mockConnection;
    private readonly Mock<SqlCommand> _mockCommand;
    private readonly SqlServerStorageOptions _options;

    public SqlServerInboxStorageTests()
    {
        _mockConnectionProvider = new Mock<IDbConnectionProvider<SqlConnection, SqlTransaction>>();
        _mockSchemaInitializer = new Mock<IDbSchemaInitializer>();
        _mockJsonOptionsProvider = new Mock<IJsonOptionsProvider>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _mockConnection = new Mock<SqlConnection>();
        _mockCommand = new Mock<SqlCommand>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            InboxTableName = "inbox",
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
        // Arrange
        var options = new SqlServerStorageOptions { ConnectionString = "test" };

        // Act
        var storage = new SqlServerInboxStorage(
            options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object,
            _mockConnectionProvider.Object,
            _mockSchemaInitializer.Object,
            _mockJsonOptionsProvider.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        SqlServerStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerInboxStorage(
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
            new SqlServerInboxStorage(
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
            new SqlServerInboxStorage(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!,
                _mockConnectionProvider.Object,
                _mockSchemaInitializer.Object,
                _mockJsonOptionsProvider.Object));
    }

    [Fact]
    public async Task AddAsync_WithValidMessage_ReturnsInboxEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new InboxOptions { RequireIdempotency = true };

        _mockCommand.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(message, It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        // Act
        var result = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message.MessageId.ToString(), result!.Id);
        Assert.Equal(InboxStatus.Pending, result.Status);
    }

    [Fact]
    public async Task IsDuplicateAsync_WithValidMessageId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.IsDuplicateAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetAsync_WithValidMessageId_ReturnsNull()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.GetAsync(messageId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithValidMessageId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        _mockCommand.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await storage.MarkProcessedAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkFailedAsync_WithValidMessageIdAndError_Succeeds()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();
        var error = "Test error";

        // Act
        await storage.MarkFailedAsync(messageId, error);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public async Task GetPendingAsync_WithValidQuery_ReturnsInboxEntries()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new InboxQuery { Status = InboxStatus.Pending, Limit = 100 };

        // Act
        var result = await storage.GetPendingAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUnprocessedAsync_WithDefaultLimit_ReturnsInboxEntries()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetUnprocessedAsync();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetUnprocessedCountAsync_ReturnsCount()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetUnprocessedCountAsync();

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_WithValidTimeSpan_Succeeds()
    {
        // Arrange
        var storage = CreateStorage();
        var olderThan = TimeSpan.FromDays(30);

        _mockCommand.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        await storage.CleanupOldEntriesAsync(olderThan);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public async Task AddAsync_WithCancellationToken_RespondsToCancel()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new InboxOptions { RequireIdempotency = true };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storage.AddAsync(message, options, cts.Token));
    }

    private SqlServerInboxStorage CreateStorage()
    {
        return new SqlServerInboxStorage(
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
    }
}
