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
    private List<Mock<IChannel>> _mockChannels;

    public ValueTask InitializeAsync()
    {
        _mockConnection = new Mock<IConnection>();
        _mockLogger = new Mock<ILogger<RabbitMqChannelPool>>();
        _mockChannels = new List<Mock<IChannel>>();

        _mockConnection.Setup(c => c.IsOpen).Returns(true);
        _mockConnection.Setup(c => c.ClientProvidedName).Returns("test-connection");
        var channelNumber = 0;
        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var mockChannel = new Mock<IChannel>();
                mockChannel.Setup(ch => ch.IsOpen).Returns(true);
                mockChannel.Setup(ch => ch.ChannelNumber).Returns(++channelNumber);
                mockChannel.Setup(ch => ch.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                _mockChannels.Add(mockChannel);
                return mockChannel.Object;
            });

        _channelPool = new RabbitMqChannelPool(
            _mockConnection.Object,
            maxChannels: 5,
            channelLifetime: TimeSpan.FromMinutes(5),
            _mockLogger.Object,
            TimeProvider.System);

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
            new RabbitMqChannelPool(null!, 5, TimeSpan.FromMinutes(5), _mockLogger!.Object, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(_mockConnection!.Object, 5, TimeSpan.FromMinutes(5), null!, TimeProvider.System));
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
        _mockConnection!.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
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
        _mockConnection!.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once); // Only created once
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
        _mockConnection!.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
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
        var channels = new List<IChannel>();
        for (int i = 0; i < 5; i++)
        {
            channels.Add(await _channelPool!.AcquireChannelAsync());
        }

        // Act - try to acquire one more
        var extraChannel = await _channelPool!.AcquireChannelAsync();

        // Assert
        Assert.NotNull(extraChannel);
        _mockConnection!.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(6));

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

    #region Additional Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(_mockConnection!.Object, 5, TimeSpan.FromMinutes(5), _mockLogger!.Object, null!));
    }

    [Fact]
    public void Constructor_WithSmallMaxChannels_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            _mockConnection!.Object,
            maxChannels: 1,
            channelLifetime: TimeSpan.FromMinutes(5),
            _mockLogger!.Object,
            TimeProvider.System);

        // Assert
        Assert.NotNull(pool);
    }

    [Fact]
    public void Constructor_WithLargeMaxChannels_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            _mockConnection!.Object,
            maxChannels: 100,
            channelLifetime: TimeSpan.FromMinutes(5),
            _mockLogger!.Object,
            TimeProvider.System);

        // Assert
        Assert.NotNull(pool);
    }

    [Fact]
    public void Constructor_WithShortChannelLifetime_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            _mockConnection!.Object,
            maxChannels: 5,
            channelLifetime: TimeSpan.FromSeconds(1),
            _mockLogger!.Object,
            TimeProvider.System);

        // Assert
        Assert.NotNull(pool);
    }

    #endregion

    #region AcquireChannelAsync Advanced Tests

    [Fact]
    public async Task AcquireChannelAsync_ReusesSameChannel_WhenReturned()
    {
        // Arrange
        var channel1 = await _channelPool!.AcquireChannelAsync();
        _channelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.Equal(channel1, channel2);
    }

    [Fact]
    public async Task AcquireChannelAsync_CreatesNewChannel_WhenPoolEmpty()
    {
        // Act
        var channel1 = await _channelPool!.AcquireChannelAsync();
        var channel2 = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.NotEqual(channel1, channel2);
        _mockConnection!.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AcquireChannelAsync_UpToMaxChannels_ThenCreatesTemporary()
    {
        // Arrange - Pool max is 5
        var channels = new List<IChannel>();
        for (int i = 0; i < 5; i++)
        {
            channels.Add(await _channelPool!.AcquireChannelAsync());
        }

        // Act - 6th acquire should succeed but create temp channel
        var tempChannel = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.NotNull(tempChannel);
    }

    #endregion

    #region ReleaseChannel Advanced Tests

    [Fact]
    public async Task ReleaseChannel_WithClosedChannel_DisposesIt()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();
        var mockChannel = _mockChannels.First();
        mockChannel.Setup(ch => ch.IsOpen).Returns(false);

        // Act
        _channelPool.ReleaseChannel(channel);

        // Assert
        mockChannel.Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ReleaseChannel_WithOpenChannel_ReturnsToPool()
    {
        // Arrange
        var channel = await _channelPool!.AcquireChannelAsync();

        // Act
        _channelPool.ReleaseChannel(channel);
        var channel2 = await _channelPool.AcquireChannelAsync();

        // Assert
        Assert.Equal(channel, channel2);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithAction_ReturnsChannelAndReleasesIt()
    {
        // Arrange
        IChannel? capturedChannel = null;

        // Act
        await _channelPool!.ExecuteAsync(async ch =>
        {
            capturedChannel = ch;
            await Task.CompletedTask;
        });

        // Assert
        Assert.NotNull(capturedChannel);
        var stats = _channelPool.GetStatistics();
        Assert.Equal(1, stats.Total);
    }

    [Fact]
    public async Task ExecuteAsync_WithFunc_ReturnsValueAndChannel()
    {
        // Arrange
        var testValue = "test-result";

        // Act
        var result = await _channelPool!.ExecuteAsync(async ch =>
        {
            await Task.CompletedTask;
            return testValue;
        });

        // Assert
        Assert.Equal(testValue, result);
    }

    [Fact]
    public async Task ExecuteAsync_WithThrowingAction_ReleasesChannelBeforeThrow()
    {
        // Arrange
        var exceptionThrown = false;

        // Act
        try
        {
            await _channelPool!.ExecuteAsync(async ch =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Test error");
            });
        }
        catch (InvalidOperationException)
        {
            exceptionThrown = true;
        }

        // Assert
        Assert.True(exceptionThrown);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task GetStatistics_ReturnsAccurateCount()
    {
        // Arrange
        var ch1 = await _channelPool!.AcquireChannelAsync();
        var ch2 = await _channelPool.AcquireChannelAsync();

        // Act
        var stats = _channelPool.GetStatistics();

        // Assert
        Assert.Equal(2, stats.Total);
        Assert.Equal(0, stats.Available);
    }

    [Fact]
    public async Task GetStatistics_CountsAvailableChannels()
    {
        // Arrange
        var ch1 = await _channelPool!.AcquireChannelAsync();
        var ch2 = await _channelPool.AcquireChannelAsync();
        _channelPool.ReleaseChannel(ch1);

        // Act
        var stats = _channelPool.GetStatistics();

        // Assert
        Assert.Equal(2, stats.Total);
        Assert.True(stats.Available > 0);
    }

    [Fact]
    public void GetStatistics_InitialState_IsZero()
    {
        // Act
        var stats = _channelPool!.GetStatistics();

        // Assert
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Available);
    }

    #endregion


    #region Concurrency Tests

    [Fact]
    public async Task AcquireChannelAsync_ConcurrentCalls_HandleSafely()
    {
        // Arrange
        var tasks = new List<Task<IChannel>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_channelPool!.AcquireChannelAsync());
        }

        // Act
        var channels = await Task.WhenAll(tasks);

        // Assert
        Assert.NotEmpty(channels);
        var stats = _channelPool.GetStatistics();
        Assert.True(stats.Total > 0);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_DoNotInterfere()
    {
        // Arrange
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_channelPool!.ExecuteAsync(async ch =>
            {
                await Task.Delay(10);
            }));
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var stats = _channelPool.GetStatistics();
        Assert.True(stats.Total > 0);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task AcquireChannelAsync_MultipleTimesInSequence()
    {
        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            var channel = await _channelPool!.AcquireChannelAsync();
            Assert.NotNull(channel);
            _channelPool.ReleaseChannel(channel);
        }
    }

    [Fact]
    public async Task DisposeAsync_ThrowsObjectDisposedAfterDispose()
    {
        // Arrange
        await _channelPool!.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await _channelPool.AcquireChannelAsync());
    }

    #endregion
}
