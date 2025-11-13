<<<<<<< HEAD
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
=======
using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
>>>>>>> testing/storage
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

<<<<<<< HEAD
public class PostgreSqlDeadLetterQueueTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class PostgreSqlDeadLetterQueueTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
>>>>>>> testing/storage
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlDeadLetterQueueTests()
    {
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test;Username=test;Password=test",
            Schema = "public",
            DeadLetterTableName = "dead_letter_queue",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlDeadLetterQueue(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlDeadLetterQueue(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var dlq = new PostgreSqlDeadLetterQueue(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(dlq);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "public",
            DeadLetterTableName = "dead_letter_queue"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("public.dead_letter_queue", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom_schema",
            DeadLetterTableName = "custom_dlq"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("custom_schema.custom_dlq", fullName);
    }

    [Theory]
    [InlineData("test_schema", "dlq_table", "test_schema.dlq_table")]
    [InlineData("public", "dead_letters", "public.dead_letters")]
    [InlineData("production", "dlq", "production.dlq")]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithVariousSchemaAndTableNames_ReturnsCorrectQualifiedName(
        string schema, string tableName, string expected)
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = schema,
            DeadLetterTableName = tableName
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal(expected, fullName);
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string Content { get; set; } = "Test content";
=======
        _mockTimeProvider = new Mock<TimeProvider>();
        _mockJsonSerializer = new Mock<IJsonSerializer>();

        _options = new PostgreSqlStorageOptions
        {
            ConnectionString = "Host=localhost;Database=test",
            AutoCreateTables = false,
            DeadLetterTableName = "dead_letters",
            Schema = "public"
        };

        _mockTimeProvider
            .Setup(x => x.GetUtcNow())
            .Returns(DateTimeOffset.UtcNow);

        _mockJsonSerializer
            .Setup(x => x.SerializeToString(It.IsAny<object>(), It.IsAny<JsonSerializerOptions>()))
            .Returns("{}");

        _mockJsonSerializer
            .Setup(x => x.DeserializeFromString<Dictionary<string, object>>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(new Dictionary<string, object>());
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions { ConnectionString = "test" };

        // Act
        var queue = new PostgreSqlDeadLetterQueue(
            options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(queue);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        PostgreSqlStorageOptions? nullOptions = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PostgreSqlDeadLetterQueue(
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
            new PostgreSqlDeadLetterQueue(
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
            new PostgreSqlDeadLetterQueue(
                _options,
                _mockTimeProvider.Object,
                nullSerializer!));
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithValidMessage_ReturnsDeadLetterId()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var message = CreateTestMessage();
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "MessageHandler",
            RetryCount = 3,
            FailureTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await queue.SendToDeadLetterAsync(message, context);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithException_ReturnsDeadLetterId()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var message = CreateTestMessage();
        var exception = new InvalidOperationException("Test failure");
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "MessageHandler",
            RetryCount = 3,
            FailureTime = DateTimeOffset.UtcNow,
            Exception = exception
        };

        // Act
        var result = await queue.SendToDeadLetterAsync(message, context);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithMetadata_ReturnsDeadLetterId()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var message = CreateTestMessage();
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "MessageHandler",
            RetryCount = 1,
            FailureTime = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object> { { "custom", "value" } }
        };

        // Act
        var result = await queue.SendToDeadLetterAsync(message, context);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithValidType_ReturnsEmptyCollection()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetDeadLettersAsync<IMessage>();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithCustomLimit_ReturnsDeadLetters()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var limit = 50;

        // Act
        var result = await queue.GetDeadLettersAsync<IMessage>(limit);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RetryAsync_WithValidDeadLetterId_ReturnsTrue()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        // Act
        var result = await queue.RetryAsync<IMessage>(deadLetterId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RetryAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        // Act
        var result = await queue.RetryAsync<IMessage>(deadLetterId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DiscardAsync_WithValidDeadLetterId_ReturnsTrue()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        // Act
        var result = await queue.DiscardAsync<IMessage>(deadLetterId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DiscardAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        // Act
        var result = await queue.DiscardAsync<IMessage>(deadLetterId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_ReturnsCount()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetDeadLetterCountAsync();

        // Assert
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsStatistics()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetStatisticsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<DeadLetterStatistics>(result);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithEmptyQueue_ReturnZeros()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetStatisticsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var message = CreateTestMessage();
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "MessageHandler",
            RetryCount = 1,
            FailureTime = DateTimeOffset.UtcNow
        };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await queue.SendToDeadLetterAsync(message, context, cts.Token));
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithCancellation_RespondsToCancel()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await queue.GetDeadLettersAsync<IMessage>(100, cts.Token));
    }

    private PostgreSqlDeadLetterQueue CreateDeadLetterQueue()
    {
        return new PostgreSqlDeadLetterQueue(
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
