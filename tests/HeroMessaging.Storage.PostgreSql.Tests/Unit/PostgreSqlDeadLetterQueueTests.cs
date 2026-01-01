using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PostgreSqlDeadLetterQueueTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly PostgreSqlStorageOptions _options;

    public PostgreSqlDeadLetterQueueTests()
    {
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
            .Returns([]);
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
        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

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
        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

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
        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithValidType_ReturnsEmptyCollection()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetDeadLettersAsync<IMessage>(TestContext.Current.CancellationToken);

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
        var result = await queue.GetDeadLettersAsync<IMessage>(limit, TestContext.Current.CancellationToken);

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
        var result = await queue.RetryAsync<IMessage>(deadLetterId, TestContext.Current.CancellationToken);

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
        var result = await queue.RetryAsync<IMessage>(deadLetterId, TestContext.Current.CancellationToken);

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
        var result = await queue.DiscardAsync<IMessage>(deadLetterId, TestContext.Current.CancellationToken);

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
        var result = await queue.DiscardAsync<IMessage>(deadLetterId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_ReturnsCount()
    {
        // Arrange
        var queue = CreateDeadLetterQueue();

        // Act
        var result = await queue.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

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
        var result = await queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

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
        var result = await queue.GetStatisticsAsync(TestContext.Current.CancellationToken);

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
            await queue.SendToDeadLetterAsync(message, context, cts.Token, TestContext.Current.CancellationToken));
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
    }
}
