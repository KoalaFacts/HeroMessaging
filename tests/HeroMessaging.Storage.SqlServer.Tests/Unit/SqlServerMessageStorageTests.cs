using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerMessageStorageTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly SqlServerStorageOptions _options;

    public SqlServerMessageStorageTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            MessagesTableName = "messages",
            Schema = "dbo"
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
        var storage = new SqlServerMessageStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerMessageStorage(
                null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public async Task StoreAsync_WithValidMessage_ReturnsMessageId()
    {
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new MessageStorageOptions { Collection = "test" };

        var messageId = await storage.StoreAsync(message, options);
        Assert.NotNull(messageId);
        Assert.NotEmpty(messageId);
    }

    [Fact]
    public async Task StoreAsync_WithTtl_ReturnsMessageId()
    {
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var ttl = TimeSpan.FromHours(24);
        var options = new MessageStorageOptions { Ttl = ttl };

        var messageId = await storage.StoreAsync(message, options);
        Assert.NotNull(messageId);
    }

    [Fact]
    public async Task RetrieveAsync_WithNonExistentMessageId_ReturnsNull()
    {
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        var result = await storage.RetrieveAsync<IMessage>(messageId);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WithValidMessageId_ReturnsFalse()
    {
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        var result = await storage.DeleteAsync(messageId);
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_WithValidMessageId_ReturnsFalse()
    {
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();

        var result = await storage.ExistsAsync(messageId);
        Assert.False(result);
    }

    [Fact]
    public async Task QueryAsync_WithValidQuery_ReturnsMessages()
    {
        var storage = CreateStorage();
        var query = new MessageQuery { Collection = "test", Limit = 100 };

        var result = await storage.QueryAsync<IMessage>(query);
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<IMessage>>(result);
    }

    [Fact]
    public async Task QueryAsync_WithTimestampFilter_ReturnsMessages()
    {
        var storage = CreateStorage();
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow;
        var query = new MessageQuery { FromTimestamp = from, ToTimestamp = to, Limit = 100 };

        var result = await storage.QueryAsync<IMessage>(query);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateAsync_WithValidMessage_ReturnsFalse()
    {
        var storage = CreateStorage();
        var messageId = Guid.NewGuid().ToString();
        var message = CreateTestMessage();

        var result = await storage.UpdateAsync(messageId, message);
        Assert.False(result);
    }

    [Fact]
    public async Task CountAsync_WithoutQuery_ReturnsCount()
    {
        var storage = CreateStorage();

        var result = await storage.CountAsync();
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CountAsync_WithValidQuery_ReturnsCount()
    {
        var storage = CreateStorage();
        var query = new MessageQuery { Collection = "test" };

        var result = await storage.CountAsync(query);
        Assert.IsType<long>(result);
    }

    [Fact]
    public async Task ClearAsync_Succeeds()
    {
        var storage = CreateStorage();

        await storage.ClearAsync();
        Assert.NotNull(storage);
    }

    private SqlServerMessageStorage CreateStorage()
    {
        return new SqlServerMessageStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
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
        _mockTimeProvider?.Dispose();
        _mockJsonSerializer?.Dispose();
    }
}
