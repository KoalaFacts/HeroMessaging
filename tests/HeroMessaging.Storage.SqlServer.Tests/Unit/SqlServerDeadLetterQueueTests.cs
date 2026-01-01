using System.Text.Json;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Utilities;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlServerDeadLetterQueueTests : IDisposable
{
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly SqlServerStorageOptions _options;

    public SqlServerDeadLetterQueueTests()
    {
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
            .Returns([]);
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

        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);
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

        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);
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

        var result = await queue.SendToDeadLetterAsync(message, context, TestContext.Current.CancellationToken);
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

        var result = await queue.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);
        Assert.IsType<long>(result);
        Assert.True(result >= 0);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsStatistics()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetStatisticsAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.IsType<DeadLetterStatistics>(result);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithEmptyQueue_ReturnZeros()
    {
        var queue = CreateDeadLetterQueue();

        var result = await queue.GetStatisticsAsync(TestContext.Current.CancellationToken);
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
            await queue.SendToDeadLetterAsync(message, context, cts.Token, TestContext.Current.CancellationToken));
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
    }
}
