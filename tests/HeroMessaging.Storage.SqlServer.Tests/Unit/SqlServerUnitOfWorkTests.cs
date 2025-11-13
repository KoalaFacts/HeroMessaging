using System.Data;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage.SqlServer;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerUnitOfWorkTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly string _connectionString;

    public SqlServerUnitOfWorkTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _connectionString = "Server=localhost;Database=test;User=test;Password=test";
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange & Act
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Assert
        Assert.NotNull(uow);
        Assert.False(uow.IsTransactionActive);
        Assert.Equal(IsolationLevel.Unspecified, uow.IsolationLevel);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithTimeProvider_CreatesInstance()
    {
        // Arrange & Act
        var uow = new SqlServerUnitOfWork(_connectionString, _timeProvider);

        // Assert
        Assert.NotNull(uow);
        Assert.False(uow.IsTransactionActive);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OutboxStorage_ReturnsInstance()
    {
        // Arrange
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Act
        var outboxStorage = uow.OutboxStorage;

        // Assert
        Assert.NotNull(outboxStorage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InboxStorage_ReturnsInstance()
    {
        // Arrange
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Act
        var inboxStorage = uow.InboxStorage;

        // Assert
        Assert.NotNull(inboxStorage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QueueStorage_ReturnsInstance()
    {
        // Arrange
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Act
        var queueStorage = uow.QueueStorage;

        // Assert
        Assert.NotNull(queueStorage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageStorage_ReturnsInstance()
    {
        // Arrange
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Act
        var messageStorage = uow.MessageStorage;

        // Assert
        Assert.NotNull(messageStorage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LazyStorageProperties_ReturnSameInstanceOnMultipleCalls()
    {
        // Arrange
        var uow = new SqlServerUnitOfWork(_connectionString);

        // Act
        var outbox1 = uow.OutboxStorage;
        var outbox2 = uow.OutboxStorage;
        var inbox1 = uow.InboxStorage;
        var inbox2 = uow.InboxStorage;

        // Assert
        Assert.Same(outbox1, outbox2);
        Assert.Same(inbox1, inbox2);
    }
}

public class SqlServerUnitOfWorkFactoryTests
{
    private readonly string _connectionString;

    public SqlServerUnitOfWorkFactoryTests()
    {
        _connectionString = "Server=localhost;Database=test;User=test;Password=test";
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SqlServerUnitOfWorkFactory(null!));

        Assert.Equal("connectionString", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange & Act
        var factory = new SqlServerUnitOfWorkFactory(_connectionString);

        // Assert
        Assert.NotNull(factory);
    }
}
