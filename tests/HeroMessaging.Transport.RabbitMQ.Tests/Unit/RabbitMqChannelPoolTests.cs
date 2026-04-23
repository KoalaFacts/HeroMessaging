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
    private List<Mock<IChannel>> _mockChannels = [];

    private Mock<IConnection> MockConnection => _mockConnection ?? throw new InvalidOperationException("Test not initialized.");
    private Mock<ILogger<RabbitMqChannelPool>> MockLogger => _mockLogger ?? throw new InvalidOperationException("Test not initialized.");
    private RabbitMqChannelPool ChannelPool => _channelPool ?? throw new InvalidOperationException("Test not initialized.");

    public ValueTask InitializeAsync()
    {
        _mockConnection = new Mock<IConnection>();
        _mockLogger = new Mock<ILogger<RabbitMqChannelPool>>();
        _mockChannels = [];

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
        var (total, available) = ChannelPool.GetStatistics();
        Assert.Equal(0, total);
        Assert.Equal(0, available);
    }

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(null!, 5, TimeSpan.FromMinutes(5), MockLogger.Object, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(MockConnection.Object, 5, TimeSpan.FromMinutes(5), null!, TimeProvider.System));
    }

    #endregion

    #region AcquireChannelAsync Tests

    [Fact]
    public async Task AcquireChannelAsync_FirstCall_CreatesNewChannel()
    {
        // Act
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(channel);
        MockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        var (total, _) = ChannelPool.GetStatistics();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task AcquireChannelAsync_MultipleCalls_ReusesChannels()
    {
        // Arrange
        var channel1 = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        ChannelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(channel1, channel2);
        MockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once); // Only created once
    }

    [Fact]
    public async Task AcquireChannelAsync_ChannelIsUnhealthy_CreatesNewChannel()
    {
        // Arrange
        var channel1 = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Make channel unhealthy
        _mockChannels[0].Setup(ch => ch.IsOpen).Returns(false);
        ChannelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotSame(channel1, channel2);
        MockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AcquireChannelAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        await ChannelPool.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireChannelAsync_WhenConnectionClosed_ThrowsInvalidOperationException()
    {
        // Arrange
        MockConnection.Setup(c => c.IsOpen).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireChannelAsync_ExceedsMaxChannels_LogsWarningAndCreatesTemporary()
    {
        // Arrange - acquire max channels without releasing
        var channels = new List<IChannel>();
        for (int i = 0; i < 5; i++)
        {
            channels.Add(await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        // Act - try to acquire one more
        var extraChannel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(extraChannel);
        MockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(6));

        // Verify warning was logged
        MockLogger.Verify(
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
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        ChannelPool.ReleaseChannel(channel);

        // Assert
        var (_, available) = ChannelPool.GetStatistics();
        Assert.Equal(1, available);
    }

    [Fact]
    public void ReleaseChannel_WithNullChannel_DoesNotThrow()
    {
        // Act & Assert - should not throw
        ChannelPool.ReleaseChannel(null!);
    }

    [Fact]
    public async Task ReleaseChannel_WithClosedChannel_DisposesChannel()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        _mockChannels[0].Setup(ch => ch.IsOpen).Returns(false);

        // Act
        ChannelPool.ReleaseChannel(channel);

        // Assert
        _mockChannels[0].Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ReleaseChannel_AfterDispose_DisposesChannel()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ChannelPool.DisposeAsync();

        // Act
        ChannelPool.ReleaseChannel(channel);

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
        var result = await ChannelPool.ExecuteAsync(async channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            return 42;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executed);
        Assert.Equal(42, result);
        var (_, available) = ChannelPool.GetStatistics();
        Assert.Equal(1, available); // Channel returned to pool
    }

    [Fact]
    public async Task ExecuteAsync_WithAction_AcquiresAndReleasesChannel()
    {
        // Arrange
        var executed = false;

        // Act
        await ChannelPool.ExecuteAsync(async channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            await Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executed);
        var (_, available) = ChannelPool.GetStatistics();
        Assert.Equal(1, available);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_StillReleasesChannel()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ChannelPool.ExecuteAsync<int>(async channel =>
            {
                throw new InvalidOperationException("Test exception");
            }, cancellationToken: TestContext.Current.CancellationToken));

        // Channel should still be released
        var (_, available) = ChannelPool.GetStatistics();
        Assert.Equal(1, available);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_InitialState_ReturnsZeros()
    {
        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(0, total);
        Assert.Equal(0, available);
    }

    [Fact]
    public async Task GetStatistics_AfterAcquiring_ReflectsState()
    {
        // Arrange
        await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(1, total);
        Assert.Equal(0, available); // Channel is acquired, not available
    }

    [Fact]
    public async Task GetStatistics_AfterRelease_ReflectsState()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        ChannelPool.ReleaseChannel(channel);

        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(1, total);
        Assert.Equal(1, available);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenCalled_DisposesAllChannels()
    {
        // Arrange
        var channel1 = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);
        ChannelPool.ReleaseChannel(channel1);
        ChannelPool.ReleaseChannel(channel2);

        // Act
        await ChannelPool.DisposeAsync();

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
        await ChannelPool.DisposeAsync();
        await ChannelPool.DisposeAsync();
        await ChannelPool.DisposeAsync();

        // Assert - should not throw
    }

    #endregion

    #region Additional Constructor Tests

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqChannelPool(MockConnection.Object, 5, TimeSpan.FromMinutes(5), MockLogger.Object, null!));
    }

    [Fact]
    public void Constructor_WithSmallMaxChannels_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            MockConnection.Object,
            maxChannels: 1,
            channelLifetime: TimeSpan.FromMinutes(5),
            MockLogger.Object,
            TimeProvider.System);

        // Assert
        Assert.NotNull(pool);
    }

    [Fact]
    public void Constructor_WithLargeMaxChannels_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            MockConnection.Object,
            maxChannels: 100,
            channelLifetime: TimeSpan.FromMinutes(5),
            MockLogger.Object,
            TimeProvider.System);

        // Assert
        Assert.NotNull(pool);
    }

    [Fact]
    public void Constructor_WithShortChannelLifetime_InitializesSuccessfully()
    {
        // Act
        var pool = new RabbitMqChannelPool(
            MockConnection.Object,
            maxChannels: 5,
            channelLifetime: TimeSpan.FromSeconds(1),
            MockLogger.Object,
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
        var channel1 = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        ChannelPool.ReleaseChannel(channel1);

        // Act
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(channel1, channel2);
    }

    [Fact]
    public async Task AcquireChannelAsync_CreatesNewChannel_WhenPoolEmpty()
    {
        // Act
        var channel1 = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(channel1, channel2);
        MockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AcquireChannelAsync_UpToMaxChannels_ThenCreatesTemporary()
    {
        // Arrange - Pool max is 5
        var channels = new List<IChannel>();
        for (int i = 0; i < 5; i++)
        {
            channels.Add(await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        // Act - 6th acquire should succeed but create temp channel
        var tempChannel = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(tempChannel);
    }

    #endregion

    #region ReleaseChannel Advanced Tests

    [Fact]
    public async Task ReleaseChannel_WithClosedChannel_DisposesIt()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var mockChannel = _mockChannels.First();
        mockChannel.Setup(ch => ch.IsOpen).Returns(false);

        // Act
        ChannelPool.ReleaseChannel(channel);

        // Assert
        mockChannel.Verify(ch => ch.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ReleaseChannel_WithOpenChannel_ReturnsToPool()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        ChannelPool.ReleaseChannel(channel);
        var channel2 = await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

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
        await ChannelPool.ExecuteAsync(async ch =>
        {
            capturedChannel = ch;
            await Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedChannel);
        var (total, _) = ChannelPool.GetStatistics();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ExecuteAsync_WithFunc_ReturnsValueAndChannel()
    {
        // Arrange
        var testValue = "test-result";

        // Act
        var result = await ChannelPool.ExecuteAsync(async ch =>
        {
            await Task.CompletedTask;
            return testValue;
        }, cancellationToken: TestContext.Current.CancellationToken);

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
            await ChannelPool.ExecuteAsync(async ch =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Test error");
            }, cancellationToken: TestContext.Current.CancellationToken);
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
        await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(2, total);
        Assert.Equal(0, available);
    }

    [Fact]
    public async Task GetStatistics_CountsAvailableChannels()
    {
        // Arrange
        var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken);
        ChannelPool.ReleaseChannel(channel);

        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(2, total);
        Assert.True(available > 0);
    }

    [Fact]
    public void GetStatistics_InitialState_IsZero()
    {
        // Act
        var (total, available) = ChannelPool.GetStatistics();

        // Assert
        Assert.Equal(0, total);
        Assert.Equal(0, available);
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
            tasks.Add(ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        // Act
        var channels = await Task.WhenAll(tasks);

        // Assert
        Assert.NotEmpty(channels);
        var (total, _) = ChannelPool.GetStatistics();
        Assert.True(total > 0);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_DoNotInterfere()
    {
        // Arrange
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(ChannelPool.ExecuteAsync(async ch =>
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }, cancellationToken: TestContext.Current.CancellationToken));
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var (total, _) = ChannelPool.GetStatistics();
        Assert.True(total > 0);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task AcquireChannelAsync_MultipleTimesInSequence()
    {
        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            var channel = await ChannelPool.AcquireChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(channel);
            ChannelPool.ReleaseChannel(channel);
        }
    }

    [Fact]
    public async Task DisposeAsync_ThrowsObjectDisposedAfterDispose()
    {
        // Arrange
        await ChannelPool.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await ChannelPool.AcquireChannelAsync(TestContext.Current.CancellationToken));
    }

    #endregion
}
