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
                mockChannel.Setup(ch => ch.CloseAsync(
                    It.IsAny<ushort>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
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
    public async Task AcquireChannelAsync_WithPublisherConfirms_EnablesConfirmTracking()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object,
            5,
            TimeSpan.FromMinutes(5),
            MockLogger.Object,
            TimeProvider.System,
            publisherConfirmsEnabled: true);

        await pool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        MockConnection.Verify(connection => connection.CreateChannelAsync(
            It.Is<CreateChannelOptions>(options =>
                options.PublisherConfirmationsEnabled && options.PublisherConfirmationTrackingEnabled),
            It.IsAny<CancellationToken>()), Times.Once);
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
                It.Is<It.IsAnyType>((v, t) => v != null && (v.ToString() ?? string.Empty).Contains("pool is full")),
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
        var (total, available) = ChannelPool.GetStatistics();
        Assert.Equal(0, total);
        Assert.Equal(0, available);
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
        var result = await ChannelPool.ExecuteAsync(channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            return Task.FromResult(42);
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
        await ChannelPool.ExecuteAsync(channel =>
        {
            executed = true;
            Assert.NotNull(channel);
            return Task.CompletedTask;
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
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChannelPool.ExecuteAsync<int>(channel =>
            {
                return Task.FromException<int>(new InvalidOperationException("Test exception"));
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

    #region Temporary overflow capacity regressions

    [Fact]
    public async Task ReleaseChannel_WhenOverCapacity_ClosesTemporaryChannelInsteadOfPoolingIt()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var cancellationToken = TestContext.Current.CancellationToken;

        var retainedChannel = await pool.AcquireChannelAsync(cancellationToken);
        var overflowChannel = await pool.AcquireChannelAsync(cancellationToken);
        Assert.NotSame(retainedChannel, overflowChannel);

        pool.ReleaseChannel(overflowChannel);

        var (total, available) = pool.GetStatistics();
        Assert.Equal(1, total); // Overflow is not counted as retained pool capacity.
        Assert.Equal(0, available); // The only retained channel is still borrowed.
        _mockChannels[1].Verify(channel => channel.Dispose(), Times.Once);

        pool.ReleaseChannel(retainedChannel);
        Assert.Equal(1, pool.GetStatistics().Available);
        var reacquired = await pool.AcquireChannelAsync(cancellationToken);
        Assert.Same(retainedChannel, reacquired);
        pool.ReleaseChannel(reacquired);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPoolIsFull_DoesNotRetainTransientOperationChannel()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var cancellationToken = TestContext.Current.CancellationToken;

        var retainedChannel = await pool.AcquireChannelAsync(cancellationToken);
        IChannel? operationChannel = null;
        await pool.ExecuteAsync(async channel =>
        {
            operationChannel = channel;
            await Task.Yield();
        }, cancellationToken);

        Assert.NotNull(operationChannel);
        Assert.NotSame(retainedChannel, operationChannel);
        Assert.Equal((1, 0), pool.GetStatistics());
        _mockChannels[1].Verify(channel => channel.Dispose(), Times.Once);

        pool.ReleaseChannel(retainedChannel);
        Assert.Equal((1, 1), pool.GetStatistics());
    }

    [Fact]
    public async Task ReleaseChannel_TemporaryOverflowReturnedTwice_DisposesOnlyOnce()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var retainedChannel = await pool.AcquireChannelAsync(token);
        var overflowChannel = await pool.AcquireChannelAsync(token);

        pool.ReleaseChannel(overflowChannel);
        pool.ReleaseChannel(overflowChannel);

        Assert.Equal((1, 0), pool.GetStatistics());
        _mockChannels[1].Verify(channel => channel.Dispose(), Times.Once);

        pool.ReleaseChannel(retainedChannel);
        Assert.Equal((1, 1), pool.GetStatistics());
    }

    [Fact]
    public async Task ExecuteAsync_TemporaryOverflowOperationFails_DisposesChannelAndPreservesError()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var retainedChannel = await pool.AcquireChannelAsync(token);
        var failure = new InvalidOperationException("Work failed");

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.ExecuteAsync<int>(
                _ => Task.FromException<int>(failure), token));

        Assert.Same(failure, caught);
        Assert.Equal((1, 0), pool.GetStatistics());
        _mockChannels[1].Verify(channel => channel.Dispose(), Times.Once);

        pool.ReleaseChannel(retainedChannel);
        Assert.Equal((1, 1), pool.GetStatistics());
    }

    [Fact]
    public async Task ReleaseChannel_PooledChannelReturnedTwice_DoesNotDuplicateAnAvailableLease()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 2, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var original = await pool.AcquireChannelAsync(token);
        pool.ReleaseChannel(original);
        pool.ReleaseChannel(original);

        Assert.Equal((1, 1), pool.GetStatistics());

        var first = await pool.AcquireChannelAsync(token);
        var second = await pool.AcquireChannelAsync(token);
        Assert.NotSame(first, second);
        pool.ReleaseChannel(first);
        pool.ReleaseChannel(second);
    }

    [Fact]
    public async Task ReleaseChannel_ClosedPooledChannelReturnedTwice_RetiresOnlyOnce()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var channel = await pool.AcquireChannelAsync(TestContext.Current.CancellationToken);
        _mockChannels[0].Setup(ch => ch.IsOpen).Returns(false);

        pool.ReleaseChannel(channel);
        pool.ReleaseChannel(channel);

        Assert.Equal((0, 0), pool.GetStatistics());
        _mockChannels[0].Verify(ch => ch.Dispose(), Times.Once);
    }

    #endregion

    [Fact]
    public async Task AcquireChannelAsync_ReusedChannelAfterShutdownStarts_DoesNotEscapeOrKeepCapacity()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var channel = await pool.AcquireChannelAsync(token);
        pool.ReleaseChannel(channel);

        // Block the health check after the available channel has been removed
        // from the bag and marked borrowed. Do not use timing-based sleeps.
        var reachedHealthCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resumeHealthCheck = new ManualResetEventSlim(false);
        _mockChannels[0].Setup(mock => mock.IsOpen).Returns(() =>
        {
            reachedHealthCheck.TrySetResult();
            if (!resumeHealthCheck.Wait(TimeSpan.FromSeconds(10), token))
                throw new TimeoutException("The channel health check did not resume.");
            return true;
        });

        var pendingAcquire = Task.Run(() => pool.AcquireChannelAsync(token), token);
        await reachedHealthCheck.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        // The pool has no available channel to drain; the borrower is paused
        // inside its health check when shutdown marks the pool disposed.
        try
        {
            await pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        finally
        {
            resumeHealthCheck.Set();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingAcquire);
        Assert.Equal((0, 0), pool.GetStatistics());
        _mockChannels[0].Verify(mock => mock.Dispose(), Times.Once);
    }

    [Fact]
    public async Task AcquireChannelAsync_DisposeQueuedBeforeMultipleWaiters_DoesNotStrandBorrowers()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 3, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var token = cancellation.Token;

        var first = await pool.AcquireChannelAsync(token);
        var second = await pool.AcquireChannelAsync(token);
        pool.ReleaseChannel(first);
        pool.ReleaseChannel(second);

        // Pause both reused-channel health checks while a third acquisition
        // holds the factory semaphore. Neither paused borrower owns the semaphore yet.
        using var resumeHealthChecks = new ManualResetEventSlim(false);
        var bothChecking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothRetired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var retirements = 0;
        foreach (var mock in _mockChannels.Take(2))
        {
            mock.Setup(channel => channel.IsOpen).Returns(() =>
            {
                if (Interlocked.Increment(ref checks) == 2)
                    bothChecking.TrySetResult();
                if (!resumeHealthChecks.Wait(TimeSpan.FromSeconds(10), token))
                    throw new TimeoutException("A paused channel health check did not resume.");
                return false; // Both borrowed channels are retired before waiting for creation.
            });
            mock.Setup(channel => channel.Dispose()).Callback(() =>
            {
                if (Interlocked.Increment(ref retirements) == 2)
                    bothRetired.TrySetResult();
            });
        }

        var borrowerA = Task.Run(() => pool.AcquireChannelAsync(token), token);
        var borrowerB = Task.Run(() => pool.AcquireChannelAsync(token), token);
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFactory = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new Mock<IChannel>();
        created.Setup(channel => channel.IsOpen).Returns(true);
        created.Setup(channel => channel.ChannelNumber).Returns(123);

        try
        {
            await bothChecking.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

            MockConnection.Setup(connection => connection.CreateChannelAsync(
                    It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    factoryEntered.TrySetResult();
                    return finishFactory.Task;
                });

            // C holds the semaphore inside its async factory, then Dispose (D)
            // queues ahead of the two resumed borrowers. The old implementation
            // released and disposed the semaphore with borrowers still waiting.
            var creating = pool.AcquireChannelAsync(token);
            await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var disposing = pool.DisposeAsync().AsTask();
            Assert.False(disposing.IsCompleted);

            resumeHealthChecks.Set();
            await bothRetired.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await Task.Yield(); // Let both retirees enqueue behind the pending dispose.

            finishFactory.SetResult(created.Object);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                await creating.WaitAsync(TimeSpan.FromSeconds(10), token);
            });
            await disposing.WaitAsync(TimeSpan.FromSeconds(10), token);

            foreach (var borrower in new[] { borrowerA, borrowerB })
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                {
                    await borrower.WaitAsync(TimeSpan.FromSeconds(5), token);
                });
            }

            Assert.Equal((0, 0), pool.GetStatistics());
            MockConnection.Verify(connection => connection.CreateChannelAsync(
                It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            created.Verify(channel => channel.Dispose(), Times.Once);
            _mockChannels[0].Verify(channel => channel.Dispose(), Times.Once);
            _mockChannels[1].Verify(channel => channel.Dispose(), Times.Once);
        }
        finally
        {
            resumeHealthChecks.Set();
            finishFactory.TrySetResult(created.Object);
            cancellation.Cancel(); // Also release pending waiters when testing the RED implementation.
        }
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCall_WaitsForInFlightCleanup()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var enteredFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFactory = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new Mock<IChannel>();
        created.Setup(channel => channel.IsOpen).Returns(true);
        created.Setup(channel => channel.ChannelNumber).Returns(301);

        MockConnection.Setup(connection => connection.CreateChannelAsync(
                It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                enteredFactory.TrySetResult();
                return completeFactory.Task;
            });

        var pendingAcquire = pool.AcquireChannelAsync(token);
        await enteredFactory.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        // The first disposal has marked the pool closed, but must wait for
        // the in-flight creation to complete before it can finish cleaning up.
        var firstDispose = pool.DisposeAsync().AsTask();
        Assert.False(firstDispose.IsCompleted);
        var secondDispose = pool.DisposeAsync().AsTask();
        try
        {
            // A concurrent caller must not observe a completed DisposeAsync
            // while the first caller is still waiting to release resources.
            Assert.False(secondDispose.IsCompleted);
        }
        finally
        {
            completeFactory.TrySetResult(created.Object);
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(10), token);
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingAcquire);
        await secondDispose.WaitAsync(TimeSpan.FromSeconds(10), token);
        created.Verify(channel => channel.Dispose(), Times.Once);
        Assert.Equal((0, 0), pool.GetStatistics());
    }

    #region Disposal during asynchronous channel creation regressions

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcquireChannelAsync_DisposedDuringChannelCreation_DoesNotEscapeOrKeepCapacity(bool overflow)
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;
        IChannel? retained = overflow ? await pool.AcquireChannelAsync(token) : null;

        var created = new Mock<IChannel>();
        created.Setup(channel => channel.IsOpen).Returns(true);
        created.Setup(channel => channel.ChannelNumber).Returns(123);

        var enteredFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFactory = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        MockConnection.Setup(connection => connection.CreateChannelAsync(
                It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                enteredFactory.TrySetResult(true);
                return completeFactory.Task;
            });

        var pendingAcquire = pool.AcquireChannelAsync(token);
        await enteredFactory.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        // DisposeAsync sets _disposed before yielding for a pending creation.
        var pendingDispose = pool.DisposeAsync().AsTask();
        completeFactory.TrySetResult(created.Object);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendingAcquire);
        await pendingDispose.WaitAsync(TimeSpan.FromSeconds(10), token);
        created.Verify(channel => channel.Dispose(), Times.Once);

        if (retained is not null)
            pool.ReleaseChannel(retained);

        Assert.Equal((0, 0), pool.GetStatistics());
    }

    #endregion


    #region Consumer-owned channel lease retirement regressions

    [Fact]
    public async Task RetireConsumerChannel_ConcurrentCallbacks_RetiresBorrowedCapacityOnlyOnce()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;

        var ownedChannel = await pool.AcquireChannelAsync(token);
        Assert.Equal((1, 0), pool.GetStatistics());

        // The consumer may retire its lease explicitly while a broker-shutdown
        // callback races to retire the same lease. Both paths must be idempotent.
        _mockChannels[0].Setup(channel => channel.IsOpen).Returns(false);
        ownedChannel.Dispose();
        var retirements = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => pool.RetireConsumerChannel(ownedChannel), token));
        await Task.WhenAll(retirements);

        Assert.Equal((0, 0), pool.GetStatistics());
        var replacement = await pool.AcquireChannelAsync(token);
        Assert.NotSame(ownedChannel, replacement);
        Assert.Equal((1, 0), pool.GetStatistics());

        pool.ReleaseChannel(replacement);
        Assert.Equal((1, 1), pool.GetStatistics());
        _mockChannels[0].Verify(channel => channel.Dispose(), Times.Once);
    }

    [Fact]
    public async Task RetireConsumerChannel_AfterPoolShutdown_DrainsBorrowedCapacityWithoutDoubleDisposal()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var channel = await pool.AcquireChannelAsync(TestContext.Current.CancellationToken);

        // A borrowed consumer channel cannot be drained from the reusable bag.
        await pool.DisposeAsync();
        Assert.Equal((1, 0), pool.GetStatistics());

        pool.RetireConsumerChannel(channel);
        pool.RetireConsumerChannel(channel);
        pool.ReleaseChannel(channel);
        pool.ReleaseChannel(channel);

        Assert.Equal((0, 0), pool.GetStatistics());
        _mockChannels[0].Verify(mock => mock.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ReleaseChannel_RetiredBorrowerRecoveredBeforeReturn_DisposesWrapperExactlyOnce()
    {
        await using var pool = new RabbitMqChannelPool(
            MockConnection.Object, maxChannels: 1, TimeSpan.FromMinutes(5),
            MockLogger.Object, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;
        var borrowed = await pool.AcquireChannelAsync(token);
        Assert.Equal((1, 0), pool.GetStatistics());

        // The broker shutdown callback retires this same retained marker.
        // With automatic recovery, the outer IChannel may reopen before the
        // publisher reaches its finally/ReleaseChannel path.
        _mockChannels[0].Setup(channel => channel.IsOpen).Returns(false);
        pool.RetireConsumerChannel(borrowed);
        Assert.Equal((0, 0), pool.GetStatistics());
        _mockChannels[0].Setup(channel => channel.IsOpen).Returns(true);

        pool.ReleaseChannel(borrowed);
        pool.ReleaseChannel(borrowed);
        Assert.Equal((0, 0), pool.GetStatistics());
        _mockChannels[0].Verify(channel => channel.Dispose(), Times.Once);

        var replacement = await pool.AcquireChannelAsync(token);
        Assert.NotSame(borrowed, replacement);
        pool.ReleaseChannel(replacement);
        Assert.Equal((1, 1), pool.GetStatistics());
    }

    #endregion
}
