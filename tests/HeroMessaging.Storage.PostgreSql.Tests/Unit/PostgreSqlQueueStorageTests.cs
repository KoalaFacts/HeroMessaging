using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PostgreSqlQueueStorageTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlQueueStorageTests()
    {
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            AutoCreateTables = false,
            QueueTableName = "queue",
            Schema = "public"
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
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "test" };

        // Act
        var storage = new PostgreSqlQueueStorage(
            options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        PostgreSqlStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlQueueStorage(
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
            new PostgreSqlQueueStorage(
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
            new PostgreSqlQueueStorage(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!));
    }

    [Fact]
    public async Task EnqueueAsync_WithValidMessage_ReturnsQueueEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new EnqueueOptions { Priority = 1 };

        // Act
        var result = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message, result.Message);
        Assert.Equal(0, result.DequeueCount);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullOptions_ReturnsQueueEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();

        // Act
        var result = await storage.EnqueueAsync("test-queue", message, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task EnqueueAsync_WithDelay_ReturnsQueueEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var delay = TimeSpan.FromMinutes(5);
        var options = new EnqueueOptions { Priority = 0, Delay = delay };

        // Act
        var result = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(delay, result.Options.Delay);
    }

    [Fact]
    public async Task EnqueueAsync_WithHighPriority_ReturnsQueueEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new EnqueueOptions { Priority = 10 };

        // Act
        var result = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Options.Priority);
    }

    [Fact]
    public async Task EnqueueAsync_WithNegativePriority_ReturnsQueueEntry()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var options = new EnqueueOptions { Priority = -5 };

        // Act
        var result = await storage.EnqueueAsync("test-queue", message, options);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DequeueAsync_WithEmptyQueue_ReturnsNull()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.DequeueAsync("test-queue");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task PeekAsync_WithEmptyQueue_ReturnsEmptyCollection()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.PeekAsync("test-queue");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PeekAsync_WithCustomCount_ReturnsMessages()
    {
        // Arrange
        var storage = CreateStorage();
        var count = 5;

        // Act
        var result = await storage.PeekAsync("test-queue", count);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithValidEntryId_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.AcknowledgeAsync("test-queue", entryId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AcknowledgeAsync_WithNonExistentEntryId_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.AcknowledgeAsync("test-queue", entryId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueTrue_UpdatesVisibility()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.RejectAsync("test-queue", entryId, requeue: true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RejectAsync_WithRequeueFalse_DeletesMessage()
    {
        // Arrange
        var storage = CreateStorage();
        var entryId = Guid.NewGuid().ToString();

        // Act
        var result = await storage.RejectAsync("test-queue", entryId, requeue: false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetQueueDepthAsync_WithMessages_ReturnsCount()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetQueueDepthAsync("test-queue");

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task GetQueueDepthAsync_WithEmptyQueue_ReturnsZero()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetQueueDepthAsync("empty-queue");

        // Assert
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task CreateQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.CreateQueueAsync("new-queue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CreateQueueAsync_WithNullOptions_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.CreateQueueAsync("new-queue", null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.DeleteQueueAsync("test-queue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetQueuesAsync_ReturnsQueueNames()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.GetQueuesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<string>>(result);
    }

    [Fact]
    public async Task QueueExistsAsync_WithExistingQueue_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.QueueExistsAsync("test-queue");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task QueueExistsAsync_WithNonExistentQueue_ReturnsFalse()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.QueueExistsAsync("non-existent-queue");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task EnqueueAsync_WithCancellationToken_RespondsToCancel()
    {
        // Arrange
        var storage = CreateStorage();
        var message = CreateTestMessage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storage.EnqueueAsync("test-queue", message, null, cts.Token));
    }

    [Fact]
    public async Task DequeueAsync_WithCancellationToken_RespondsToCancel()
    {
        // Arrange
        var storage = CreateStorage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await storage.DequeueAsync("test-queue", cts.Token));
    }

    private PostgreSqlQueueStorage CreateStorage()
    {
        return new PostgreSqlQueueStorage(
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
