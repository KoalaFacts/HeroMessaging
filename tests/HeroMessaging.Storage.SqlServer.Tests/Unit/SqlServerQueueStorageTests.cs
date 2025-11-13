using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerQueueStorageTests
{
    private readonly Mock<IJsonSerializer> _mockJsonSerializer;
    private readonly FakeTimeProvider _timeProvider;
    private readonly SqlServerStorageOptions _options;

    public SqlServerQueueStorageTests()
    {
        _mockJsonSerializer = new Mock<IJsonSerializer>();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        _options = new SqlServerStorageOptions
        {
            ConnectionString = "Server=localhost;Database=test;User=test;Password=test",
            Schema = "dbo",
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
            new SqlServerQueueStorage(null!, _timeProvider, _mockJsonSerializer.Object));

        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(_options, null!, _mockJsonSerializer.Object));

        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullJsonSerializer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(_options, _timeProvider, null!));

        Assert.Equal("jsonSerializer", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange
        var invalidOptions = new SqlServerStorageOptions
        {
            ConnectionString = null!,
            Schema = "dbo",
            QueueTableName = "queues",
            AutoCreateTables = false
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerQueueStorage(invalidOptions, _timeProvider, _mockJsonSerializer.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var storage = new SqlServerQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            QueueTableName = "queues"
        };

        // Act
        var fullName = options.GetFullTableName(options.QueueTableName);

        // Assert
        Assert.Equal("[dbo].[queues]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom_schema",
            QueueTableName = "custom_queues"
        };

        // Act
        var fullName = options.GetFullTableName(options.QueueTableName);

        // Assert
        Assert.Equal("[custom_schema].[custom_queues]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateQueueAsync_WithValidQueueName_ReturnsTrue()
    {
        // Arrange
        var storage = new SqlServerQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Act
        var result = await storage.CreateQueueAsync("test-queue");

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateQueueAsync_WithNullOptions_ReturnsTrue()
    {
        // Arrange
        var storage = new SqlServerQueueStorage(_options, _timeProvider, _mockJsonSerializer.Object);

        // Act
        var result = await storage.CreateQueueAsync("test-queue", null);

        // Assert
        Assert.True(result);
    }

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
