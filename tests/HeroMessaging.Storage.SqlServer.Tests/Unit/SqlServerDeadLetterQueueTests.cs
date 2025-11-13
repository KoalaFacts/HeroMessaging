<<<<<<< HEAD
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Storage.SqlServer;
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

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

<<<<<<< HEAD
public class SqlServerDeadLetterQueueTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
=======
[Trait("Category", "Unit")]
public sealed class SqlServerDeadLetterQueueTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
>>>>>>> testing/storage
    private readonly SqlServerStorageOptions _options;

    public SqlServerDeadLetterQueueTests()
    {
<<<<<<< HEAD
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
            DeadLetterTableName = "DeadLetterQueue",
            AutoCreateTables = false
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var dlq = new SqlServerDeadLetterQueue(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(dlq);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            DeadLetterTableName = "DeadLetterQueue"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("[dbo].[DeadLetterQueue]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            DeadLetterTableName = "CustomDLQ"
        };

        // Act
        var fullName = options.GetFullTableName(options.DeadLetterTableName);

        // Assert
        Assert.Equal("[custom_schema].[CustomDLQ]", fullName);
    }

    [Theory]
    [InlineData("test_schema", "dlq_table", "[test_schema].[dlq_table]")]
    [InlineData("dbo", "DeadLetters", "[dbo].[DeadLetters]")]
    [InlineData("production", "DLQ", "[production].[DLQ]")]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithVariousSchemaAndTableNames_ReturnsCorrectQualifiedName(
        string schema, string tableName, string expected)
    {
        // Arrange
        var options = new SqlServerStorageOptions
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

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test",
            AutoCreateTables = false,
            DeadLetterTableName = "dead_letters",
            Schema = "dbo"
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
        var queue = new SqlServerDeadLetterQueue(
            _options,
            _mockTimeProvider.Object,
            _mockJsonSerializer.Object);
        Assert.NotNull(queue);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(
                null!,
                _mockTimeProvider.Object,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(
                _options,
                null!,
                _mockJsonSerializer.Object));
    }

    [Fact]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerDeadLetterQueue(
                _options,
                _mockTimeProvider.Object,
                null!));
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithValidMessage_ReturnsDeadLetterId()
    {
        var queue = CreateDeadLetterQueue();
        var message = CreateTestMessage();
        var context = new DeadLetterContext
        {
            Reason = "Processing failed",
            Component = "MessageHandler",
            RetryCount = 3,
            FailureTime = DateTimeOffset.UtcNow
        };

        var result = await queue.SendToDeadLetterAsync(message, context);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithException_ReturnsDeadLetterId()
    {
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

        var result = await queue.SendToDeadLetterAsync(message, context);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithMetadata_ReturnsDeadLetterId()
    {
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

        var result = await queue.SendToDeadLetterAsync(message, context);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithValidType_ReturnsEmptyCollection()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetDeadLettersAsync<IMessage>();
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithCustomLimit_ReturnsDeadLetters()
    {
        var queue = CreateDeadLetterQueue();
        var limit = 50;

        var result = await queue.GetDeadLettersAsync<IMessage>(limit);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RetryAsync_WithValidDeadLetterId_ReturnsFalse()
    {
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        var result = await queue.RetryAsync<IMessage>(deadLetterId);
        Assert.False(result);
    }

    [Fact]
    public async Task DiscardAsync_WithValidDeadLetterId_ReturnsFalse()
    {
        var queue = CreateDeadLetterQueue();
        var deadLetterId = Guid.NewGuid().ToString();

        var result = await queue.DiscardAsync<IMessage>(deadLetterId);
        Assert.False(result);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_ReturnsCount()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetDeadLetterCountAsync();
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsStatistics()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetStatisticsAsync();
        Assert.NotNull(result);
        Assert.IsType<DeadLetterStatistics>(result);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithEmptyQueue_ReturnZeros()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetStatisticsAsync();
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task SendToDeadLetterAsync_WithCancellation_RespondsToCancel()
    {
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

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await queue.SendToDeadLetterAsync(message, context, cts.Token));
    }

    private SqlServerDeadLetterQueue CreateDeadLetterQueue()
    {
        return new SqlServerDeadLetterQueue(
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
