using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Integration")]
[Collection(nameof(SqlServerIdempotencyStoreCollection))]
public sealed class SqlServerQueueStorageTests : IDisposable
{
    private readonly string _queueName = $"test-queue-{Guid.NewGuid():N}";
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly SqlServerStorageOptions _options;

    public SqlServerQueueStorageTests(SqlServerIdempotencyStoreFixture fixture)
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new SqlServerStorageOptions
        {
            ConnectionString = fixture.ConnectionString,
            AutoCreateTables = true,
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

        var result = await storage.EnqueueAsync(_queueName, message, options, TestContext.Current.CancellationToken);
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

        var result = await storage.EnqueueAsync(_queueName, message, options, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(delay, result.Options.Delay);
    }

    [Fact]
    public async Task DequeueAsync_WithEmptyQueue_ReturnsNull()
    {
        var storage = CreateStorage();
        var result = await storage.DequeueAsync(_queueName, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task PeekAsync_WithEmptyQueue_ReturnsEmptyCollection()
    {
        var storage = CreateStorage();
        var result = await storage.PeekAsync(_queueName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithValidEntryId_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.AcknowledgeAsync(_queueName, entryId, TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.RejectAsync(_queueName, entryId, requeue: true, TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueFalse_ReturnsFalse()
    {
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();
        var result = await storage.RejectAsync(_queueName, entryId, requeue: false, TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task GetQueueDepthAsync_ReturnsCount()
    {
        var storage = CreateStorage();
        var result = await storage.GetQueueDepthAsync(_queueName, TestContext.Current.CancellationToken);
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CreateQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        var storage = CreateStorage();
        var result = await storage.CreateQueueAsync("new-queue", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        var storage = CreateStorage();
        var result = await storage.DeleteQueueAsync(_queueName, TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task GetQueuesAsync_ReturnsQueueNames()
    {
        var storage = CreateStorage();
        var result = await storage.GetQueuesAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<string>>(result);
    }

    [Fact]
    public async Task QueueExistsAsync_WithNonExistentQueue_ReturnsFalse()
    {
        var storage = CreateStorage();
        var result = await storage.QueueExistsAsync($"{_queueName}-missing", TestContext.Current.CancellationToken);
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
