using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerQueueStorageTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly SqlServerStorageOptions _options;

    public SqlServerQueueStorageTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            QueueTableName = "queue",
            Schema = "dbo"
        };

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(It.IsAny<IMessage>(), It.IsAny<System.Text.Json.JsonSerializerOptions>()))
            .Returns("{}");
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        var storage = new SqlServerQueueStorage(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(
                null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(
                _options,
                null!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(
                _options,
                _mockTimeProvider.Object,
                null!));
    }

    [Fact]
    public async Task EnqueueAsync_WithValidMessage_ReturnsQueueEntry()
    {
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new EnqueueOptions { Priority = 1 };

        var result = await storage.EnqueueAsync("test-queue", message, options);
        Assert.NotNull(result);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task EnqueueAsync_WithDelay_ReturnsQueueEntry()
    {
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var delay = TimeSpan.FromMinutes(5);
        var options = new EnqueueOptions { Priority = 0, Delay = delay };

        var result = await storage.EnqueueAsync("test-queue", message, options);
        Assert.NotNull(result);
        Assert.Equal(delay, result.Options.Delay);
    }

    [Fact]
    public async Task DequeueAsync_WithEmptyQueue_ReturnsNull()
    {
        var storage = CreateStorage();
        var result = await storage.DequeueAsync("test-queue");
        Assert.Null(result);
    }

    [Fact]
    public async Task PeekAsync_WithEmptyQueue_ReturnsEmptyCollection()
    {
        var storage = CreateStorage();
        var result = await storage.PeekAsync("test-queue");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithValidEntryId_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.AcknowledgeAsync("test-queue", entryId);
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.RejectAsync("test-queue", entryId, requeue: true);
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueFalse_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.RejectAsync("test-queue", entryId, requeue: false);
        Assert.False(result);
    }

    [Fact]
    public async Task GetQueueDepthAsync_ReturnsCount()
    {
        var storage = CreateStorage();
        var result = await storage.GetQueueDepthAsync("test-queue");
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CreateQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        var storage = CreateStorage();
        var result = await storage.CreateQueueAsync("new-queue");
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        var storage = CreateStorage();
        var result = await storage.DeleteQueueAsync("test-queue");
        Assert.True(result);
    }

    [Fact]
    public async Task GetQueuesAsync_ReturnsQueueNames()
    {
        var storage = CreateStorage();
        var result = await storage.GetQueuesAsync();
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<string>>(result);
    }

    [Fact]
    public async Task QueueExistsAsync_WithNonExistentQueue_ReturnsFalse()
    {
        var storage = CreateStorage();
        var result = await storage.QueueExistsAsync("non-existent-queue");
        Assert.False(result);
    }

    private SqlServerQueueStorage CreateStorage()
    {
        return new SqlServerQueueStorage(
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
    }
}
