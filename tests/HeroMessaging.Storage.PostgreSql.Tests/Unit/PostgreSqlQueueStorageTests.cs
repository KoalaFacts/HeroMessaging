<<<<<<< HEAD
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Npgsql;
=======
using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Moq;
>>>>>>> testing/storage
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

<<<<<<< HEAD
public class PostgreSqlQueueStorageTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class PostgreSqlQueueStorageTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
>>>>>>> testing/storage
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlQueueStorageTests()
    {
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
            QueueTableName = "queues",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlQueueStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlQueueStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlQueueStorage(_options, _timeProvider, null!));

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
            QueueTableName = "queues",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlQueueStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new PostgreSqlQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

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
        var storage = new PostgreSqlQueueStorage(
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
            new PostgreSqlQueueStorage(nullConnection!, transaction, _timeProvider, _mockJsonSerializer.Object));

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
            QueueTableName = "queues"
        };

        // Act
        var fullName = options.GetFullTableName(options.QueueTableName);

        // Assert
        Assert.Equal("public.queues", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            QueueTableName = "custom_queues"
        };

        // Act
        var fullName = options.GetFullTableName(options.QueueTableName);

        // Assert
        Assert.Equal("custom_schema.custom_queues", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        // Arrange
        var storage = new PostgreSqlQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Act
        var result = await storage.CreateQueueAsync("test-queue");
=======
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
>>>>>>> testing/storage

        // Assert
        Assert.True(result);
    }

    [Fact]
<<<<<<< HEAD
    [Trait("Category", "Unit")]
    public async Task CreateQueueAsync_WithNullOptions_ReturnsTrue()
    {
        // Arrange
        var storage = new PostgreSqlQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Act
        var result = await storage.CreateQueueAsync("test-queue", null);
=======
    public async Task CreateQueueAsync_WithNullOptions_ReturnsTrue()
    {
        // Arrange
        var storage = CreateStorage();

        // Act
        var result = await storage.CreateQueueAsync("new-queue", null);
>>>>>>> testing/storage

        // Assert
        Assert.True(result);
    }

<<<<<<< HEAD
    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
=======
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
>>>>>>> testing/storage
    }
}
