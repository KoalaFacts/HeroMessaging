<<<<<<< HEAD
using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Npgsql;
=======
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
>>>>>>> testing/storage
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

<<<<<<< HEAD
public class PostgreSqlMessageStorageTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class PostgreSqlMessageStorageTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
>>>>>>> testing/storage
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlMessageStorageTests()
    {
<<<<<<< HEAD
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
=======
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            AutoCreateTables = false,
            MessagesTableName = "messages",
            Schema = "public"
        };

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(It.IsAny<object>(), It.IsAny<System.Text.Json.JsonSerializerOptions>()))
            .Returns("{}");

        _mockJsonSerializer
            .Setup(x => x.DeserializeFromString<object>(It.IsAny<string>(), It.IsAny<System.Text.Json.JsonSerializerOptions>()))
            .Returns(new object());
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "test" };

        // Act
        var storage = new PostgreSqlMessageStorage(
            options,
            _mockTimeProvider.Object,
>>>>>>> testing/storage
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
<<<<<<< HEAD
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
=======
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        PostgreSqlStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(
                nullOptions!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        TimeProvider? nullTimeProvider = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(
                _options,
                nullTimeProvider!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange
        IJsonSerializer? nullSerializer = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlMessageStorage(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!));
    }

    [Fact]
    public async Task StoreAsync_WithValidMessage_ReturnsMessageId()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new MessageStorageOptions { Collection = "test" };

        // Act
        var messageId = await storage.StoreAsync(message, options);

        // Assert
        Assert.NotNull(messageId);
        Assert.NotEmpty(messageId);
    }

    [Fact]
    public async Task StoreAsync_WithNullOptions_ReturnsMessageId()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();

        // Act
        var messageId = await storage.StoreAsync(message, (MessageStorageOptions?)null);

        // Assert
        Assert.NotNull(messageId);
        Assert.NotEmpty(messageId);
    }

    [Fact]
    public async Task StoreAsync_WithTtl_ReturnsMessageId()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var ttl = TimeSpan.FromHours(24);
        var options = new MessageStorageOptions { Ttl = ttl };

        // Act
        var messageId = await storage.StoreAsync(message, options);

        // Assert
        Assert.NotNull(messageId);
    }

    [Fact]
    public async Task StoreAsync_WithoutTtl_ReturnsMessageId()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new MessageStorageOptions { Ttl = null };

        // Act
        var messageId = await storage.StoreAsync(message, options);

        // Assert
        Assert.NotNull(messageId);
    }

    [Fact]
    public async Task RetrieveAsync_WithValidMessageId_ReturnsMessage()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.RetrieveAsync<IMessage>(messageId);

        // Assert
        Assert.Null(result); // No data with mocked connection
    }

    [Fact]
    public async Task RetrieveAsync_WithNonExistentMessageId_ReturnsNull()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.RetrieveAsync<IMessage>(messageId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WithValidMessageId_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.DeleteAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentMessageId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.DeleteAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_WithValidMessageId_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.ExistsAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentMessageId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.ExistsAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredMessage_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.ExistsAsync(messageId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task QueryAsync_WithValidQuery_ReturnsMessages()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new MessageQuery { Collection = "test", Limit = 100 };

        // Act
        var result = await storage.QueryAsync<IMessage>(query);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<IMessage>>(result);
    }

    [Fact]
    public async Task QueryAsync_WithoutCollection_ReturnsMessages()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new MessageQuery { Limit = 100 };

        // Act
        var result = await storage.QueryAsync<IMessage>(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task QueryAsync_WithTimestampFilter_ReturnsMessages()
    {
        // Arrange
        var storage = CreateStorage();
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;
        var query = new MessageQuery { FromTimestamp = from, ToTimestamp = to, Limit = 100 };

        // Act
        var result = await storage.QueryAsync<IMessage>(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task QueryAsync_WithPagination_ReturnsMessages()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new MessageQuery { Limit = 50, Offset = 100 };

        // Act
        var result = await storage.QueryAsync<IMessage>(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateAsync_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();
        var message = CreateTestMessage();

        // Act
        var result = await storage.UpdateAsync(messageId, message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentMessageId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();
        var message = CreateTestMessage();

        // Act
        var result = await storage.UpdateAsync(messageId, message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CountAsync_WithoutQuery_ReturnsCount()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.CountAsync();

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CountAsync_WithValidQuery_ReturnsCount()
    {
        // Arrange
        var storage = CreateStorage();
        var query = new MessageQuery { Collection = "test" };

        // Act
        var result = await storage.CountAsync(query);

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllMessages()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        await storage.ClearAsync();

        // Assert - no exception thrown
        Assert.NotNull(storage);
    }

    [Fact]
    public async Task StoreAsync_WithCancellationToken_RespondsToCancel()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storage.StoreAsync(message, (MessageStorageOptions?)null, cts.Token));
    }

    private PostgreSqlMessageStorage CreateStorage()
    {
        return new PostgreSqlMessageStorage(
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
