using HeroMessaging.Transport.RabbitMQ.Connection;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

/// <summary>
/// Unit tests for RabbitMqChannelPool
/// Target: 100% coverage for channel pool logic
/// </summary>
[Trait("Category", "Unit")]
public class RabbitMqChannelPoolTests : IAsyncLifetime
{
    private Mock<IConnection>? _mockConnection;
    private Mock<ILogger<RabbitMqChannelPool>>? _mockLogger;
    private RabbitMqChannelPool? _channelPool;
    private List<Mock<IModel>> _mockChannels;

    public ValueTask InitializeAsync()
    {
        _mockConnection = new Mock<IConnection>();
        _mockLogger = new Mock<ILogger<RabbitMqChannelPool>>();
        _mockChannels = new List<Mock<IModel>>();

        // Setup connection
        _mockConnection.Setup(c => c.IsOpen).Returns(true);
        _mockConnection.Setup(c => c.ClientProvidedName).Returns("test-connection");

        // Setup channel creation
        var channelNumber = 0;
        _mockConnection.Setup(c => c.CreateModel()).Returns(() =>
        {
            var mockChannel = new Mock<IModel>();
            mockChannel.Setup(ch => ch.IsOpen).Returns(true);
            mockChannel.Setup(ch => ch.ChannelNumber).Returns(++channelNumber);
            _mockChannels.Add(mockChannel);
            return mockChannel.Object;
        });

        _channelPool = new RabbitMqChannelPool(
            _mockConnection.Object,
            maxChannels: 5,
            channelLifetime: TimeSpan.FromMinutes(5),
            _mockLogger.Object);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channelPool != null)
        {
            await _channelPool.DisposeAsync();
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_InitializesSuccessfully()
    {
        // Assert
        Assert.NotNull(_channelPool);
        var stats = _channelPool!.GetStatistics();
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Available);
    }

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(null!, 5, TimeSpan.FromMinutes(5), _mockLogger!.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(_mockConnection!.Object, 5, TimeSpan.FromMinutes(5), null!));
    }

    #endregion

    #region AcquireChannelAsync Tests

    [Fact]
    public async Task AcquireChannelAsync_FirstCall_CreatesNewChannel()
    {
        // Act
        var channel = await _channelPool!.AcquireChannelAsync();

        // Assert
        Assert.NotNull(channel);
        _mockConnection!.Verify(c => c.CreateModel(), Times.Once);
        var stats = _channelPool.GetStatistics();
        Assert.Equal(1, stats.Total);
    }

    [Fact]
    public async Task AcquireChannelAsync_MultipleCalls_ReusesChannels()
    {
        // Arrange
        var channel1 = await _channelPool!.AcquireChannelAsync();
        _channelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.Same(channel1, channel2);
        _mockConnection!.Verify(c => c.CreateModel(), Times.Once); // Only created once
    }

    [Fact]
    public async Task AcquireChannelAsync_ChannelIsUnhealthy_CreatesNewChannel()
    {
        // Arrange
        var channel1 = await _channelPool!.AcquireChannelAsync();

        // Make channel unhealthy
        _mockChannels[0].Setup(ch => ch.IsOpen).Returns(false);
        _channelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.NotSame(channel1, channel2);
        _mockConnection!.Verify(c => c.CreateModel(), Times.Exactly(2));
    }

    [Fact]
    public async Task AcquireChannelAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        await _channelPool!.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await _channelPool.AcquireChannelAsync());
    }

    [Fact]
    public async Task AcquireChannelAsync_WhenConnectionClosed_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockConnection!.Setup(c => c.IsOpen).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _channelPool!.AcquireChannelAsync());
    }

    [Fact]
    public async Task AcquireChannelAsync_ExceedsMaxChannels_LogsWarningAndCreatesTemporary()
    {
        // Arrange - acquire max channels without releasing
        var channels = new List<IModel>();
        for (int i = 0; i < 5; i++)
        {
            channels.Add(await _channelPool!.AcquireChannelAsync());
        }

        // Act - try to acquire one more
        var extraChannel = await _channelPool!.AcquireChannelAsync();

        // Assert
        Assert.NotNull(extraChannel);
        _mockConnection!.Verify(c => c.CreateModel(), Times.Exactly(6));

        // Verify warning was logged
        _mockLogger!.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("pool is full")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ReleaseChannel Tests

    [Fact]
    public async Task ReleaseChannel_WithValidChannel_ReturnsToPool()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();

        // Act
        _channelPool.ReleaseChannel(channel);

        // Assert
        var stats = _channelPool.GetStatistics();
        Assert.Equal(1, stats.Available);
    }

    [Fact]
    public void ReleaseChannel_WithNullChannel_DoesNotThrow()
    {
        // Act & Assert - should not throw
        _channelPool!.ReleaseChannel(null!);
    }

    [Fact]
    public async Task ReleaseChannel_WithClosedChannel_DisposesChannel()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();
        _mockChannels[0].Setup(ch => ch.IsOpen).Returns(false);

        // Act
        _channelPool.ReleaseChannel(channel);

        // Assert
        _mockChannels[0].Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ReleaseChannel_AfterDispose_DisposesChannel()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();
        await _channelPool.DisposeAsync();

        // Act
        _channelPool.ReleaseChannel(channel);

        // Assert
        _mockChannels[0].Verify(ch => ch.Dispose(), Times.AtLeastOnce);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithFunction_AcquiresAndReleasesChannel()
    {
        // Arrange
        var executed = false;

        // Act
        var result = await _channelPool!.ExecuteAsync(async channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            return 42;
        });

        // Assert
        Assert.True(executed);
        Assert.Equal(42, result);
        var stats = _channelPool.GetStatistics();
        Assert.Equal(1, stats.Available); // Channel returned to pool
    }

    [Fact]
    public async Task ExecuteAsync_WithAction_AcquiresAndReleasesChannel()
    {
        // Arrange
        var executed = false;

        // Act
        await _channelPool!.ExecuteAsync(async channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            await Task.CompletedTask;
        });

        // Assert
        Assert.True(executed);
        var stats = _channelPool.GetStatistics();
        Assert.Equal(1, stats.Available);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_StillReleasesChannel()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _channelPool!.ExecuteAsync<int>(async channel =>
            {
                throw new InvalidOperationException("Test exception");
            }));

        // Channel should still be released
        var stats = _channelPool!.GetStatistics();
        Assert.Equal(1, stats.Available);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_InitialState_ReturnsZeros()
    {
        // Act
        var stats = _channelPool!.GetStatistics();

        // Assert
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Available);
    }

    [Fact]
    public async Task GetStatistics_AfterAcquiring_ReflectsState()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();

        // Act
        var stats = _channelPool.GetStatistics();

        // Assert
        Assert.Equal(1, stats.Total);
        Assert.Equal(0, stats.Available); // Channel is acquired, not available
    }

    [Fact]
    public async Task GetStatistics_AfterRelease_ReflectsState()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();
        _channelPool.ReleaseChannel(channel);

        // Act
        var stats = _channelPool.GetStatistics();

        // Assert
        Assert.Equal(1, stats.Total);
        Assert.Equal(1, stats.Available);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenCalled_DisposesAllChannels()
    {
        // Arrange
        var channel1 = await _channelPool!.AcquireChannelAsync();
        var channel2 = await _channelPool.AcquireChannelAsync();
        _channelPool.ReleaseChannel(channel1);
        _channelPool.ReleaseChannel(channel2);

        // Act
        await _channelPool.DisposeAsync();

        // Assert
        foreach (var mockChannel in _mockChannels)
        {
            mockChannel.Verify(ch => ch.Dispose(), Times.Once);
        }
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        // Act
        await _channelPool!.DisposeAsync();
        await _channelPool.DisposeAsync();
        await _channelPool.DisposeAsync();

        // Assert - should not throw
    }

    #endregion
}
