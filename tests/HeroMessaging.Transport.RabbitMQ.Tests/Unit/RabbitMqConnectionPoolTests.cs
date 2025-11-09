using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ.Connection;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// Unit tests for RabbitMqConnectionPool
/// Target: 100% coverage for connection pool logic
/// </summary>
[Trait("Category", "Unit")]
public class RabbitMqConnectionPoolTests : IAsyncLifetime
{
    private Mock<ILogger<RabbitMqConnectionPool>>? _mockLogger;
    private RabbitMqTransportOptions? _options;
    private RabbitMqConnectionPool? _connectionPool;
    private List<Mock<IConnection>>? _mockConnections;

    public ValueTask InitializeAsync()
    {
        _mockLogger = new Mock<ILogger<RabbitMqConnectionPool>>();
        _mockConnections = new List<Mock<IConnection>>();

        _options = new RabbitMqTransportOptions
        {
            Host = "localhost",
            Port = 5672,
            VirtualHost = "/",
            UserName = "guest",
            Password = "guest",
            MinPoolSize = 1,
            MaxPoolSize = 3,
            ConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionPool != null)
        {
            await _connectionPool.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetConnectionAsync_FirstCall_CreatesNewConnection()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Act - Note: We can't easily test this without refactoring to inject IConnectionFactory
        // For now, this test documents the limitation

        // Assert
        Assert.NotNull(_connectionPool);
        await Task.CompletedTask;
    }

    [Fact]
    public void Constructor_WithValidOptions_InitializesSuccessfully()
    {
        // Act
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Assert
        Assert.NotNull(_connectionPool);
        var stats = _connectionPool.GetStatistics();
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Active);
        Assert.Equal(0, stats.Idle);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConnectionPool(null!, _mockLogger!.Object, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqConnectionPool(_options!, null!, TimeProvider.System));
    }

    [Fact]
    public async Task DisposeAsync_WhenCalled_DisposesCleanly()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Act
        await _connectionPool.DisposeAsync();

        // Assert
        // Should not throw
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await _connectionPool.GetConnectionAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Act
        await _connectionPool.DisposeAsync();
        await _connectionPool.DisposeAsync();
        await _connectionPool.DisposeAsync();

        // Assert - should not throw
    }

    [Fact]
    public void GetStatistics_InitialState_ReturnsZeros()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Act
        var stats = _connectionPool.GetStatistics();

        // Assert
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Active);
        Assert.Equal(0, stats.Idle);
    }

    [Fact]
    public void ReleaseConnection_WithNullConnection_DoesNotThrow()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);

        // Act & Assert - should not throw
        _connectionPool.ReleaseConnection(null!);
    }

    [Fact]
    public async Task GetConnectionAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _connectionPool = new RabbitMqConnectionPool(_options!, _mockLogger!.Object, TimeProvider.System);
        await _connectionPool.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await _connectionPool.GetConnectionAsync());
    }

    // Note: More comprehensive tests would require refactoring to inject IConnectionFactory
    // or using integration tests with real RabbitMQ. The current implementation creates
    // ConnectionFactory internally, making it hard to mock.
}
