using HeroMessaging.RingBuffer.WaitStrategies;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.WaitStrategies;

[Trait("Category", "Unit")]
public class WaitStrategyTests
{
    [Fact]
    public async Task BlockingWaitStrategy_WaitFor_ReturnsSequence()
    {
        // Arrange
        var strategy = new BlockingWaitStrategy();
        const long expectedSequence = 42;

        // Act - Signal in parallel
        var waitTask = StartWaiter(strategy, expectedSequence);
        var result = await SignalUntilCompletedAsync(strategy, waitTask);

        // Assert
        Assert.Equal(expectedSequence, result);
    }

    [Fact]
    public void BlockingWaitStrategy_SignalWithoutWaiter_DoesNotThrow()
    {
        // Arrange
        var strategy = new BlockingWaitStrategy();

        // Act & Assert
        strategy.SignalAllWhenBlocking(); // Should not throw
    }

    [Fact]
    public void SleepingWaitStrategy_WaitFor_ReturnsSequence()
    {
        // Arrange
        var strategy = new SleepingWaitStrategy();
        const long expectedSequence = 42;

        // Act
        var result = strategy.WaitFor(expectedSequence);

        // Assert
        Assert.Equal(expectedSequence, result);
    }

    [Fact]
    public void SleepingWaitStrategy_SignalAllWhenBlocking_DoesNotThrow()
    {
        // Arrange
        var strategy = new SleepingWaitStrategy();

        // Act & Assert
        strategy.SignalAllWhenBlocking(); // Should not throw (no-op)
    }

    [Fact]
    public void YieldingWaitStrategy_WaitFor_ReturnsSequence()
    {
        // Arrange
        var strategy = new YieldingWaitStrategy();
        const long expectedSequence = 42;

        // Act
        var result = strategy.WaitFor(expectedSequence);

        // Assert
        Assert.Equal(expectedSequence, result);
    }

    [Fact]
    public void YieldingWaitStrategy_SignalAllWhenBlocking_DoesNotThrow()
    {
        // Arrange
        var strategy = new YieldingWaitStrategy();

        // Act & Assert
        strategy.SignalAllWhenBlocking(); // Should not throw (no-op)
    }

    [Fact]
    public void BusySpinWaitStrategy_SignalAllWhenBlocking_DoesNotThrow()
    {
        // Arrange
        var strategy = new BusySpinWaitStrategy();

        // Act & Assert
        strategy.SignalAllWhenBlocking(); // Should not throw (no-op)
    }

    [Fact]
    public async Task TimeoutBlockingWaitStrategy_WaitFor_WithSignal_ReturnsSequence()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(1);
        var strategy = new TimeoutBlockingWaitStrategy(timeout);
        const long expectedSequence = 42;

        // Act - Signal in parallel
        var waitTask = StartWaiter(strategy, expectedSequence);
        var result = await SignalUntilCompletedAsync(strategy, waitTask);

        // Assert
        Assert.Equal(expectedSequence, result);
    }

    [Fact]
    public async Task TimeoutBlockingWaitStrategy_MultipleWaiters_AllSignaled()
    {
        // Arrange
        var strategy = new TimeoutBlockingWaitStrategy(TimeSpan.FromSeconds(1));
        const int waiterCount = 5;
        var tasks = new Task<long>[waiterCount];

        // Act
        for (int i = 0; i < waiterCount; i++)
        {
            int sequence = i;
            tasks[i] = StartWaiter(strategy, sequence);
        }

        var results = await SignalUntilCompletedAsync(strategy, Task.WhenAll(tasks));

        // Assert
        for (int i = 0; i < waiterCount; i++)
        {
            Assert.Equal(i, results[i]);
        }
    }

    [Fact]
    public void TimeoutBlockingWaitStrategy_WaitFor_WithoutSignal_ThrowsTimeoutException()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(100);
        var strategy = new TimeoutBlockingWaitStrategy(timeout);
        const long sequence = 42;

        // Act & Assert
        var exception = Assert.Throws<TimeoutException>(() => strategy.WaitFor(sequence));
        Assert.Contains(sequence.ToString(), exception.Message);
        Assert.Contains(timeout.ToString(), exception.Message);
    }

    [Fact]
    public void TimeoutBlockingWaitStrategy_SignalWithoutWaiter_DoesNotThrow()
    {
        // Arrange
        var strategy = new TimeoutBlockingWaitStrategy(TimeSpan.FromSeconds(1));

        // Act & Assert
        strategy.SignalAllWhenBlocking(); // Should not throw
    }

    [Fact]
    public async Task BlockingWaitStrategy_MultipleWaiters_AllSignaled()
    {
        // Arrange
        var strategy = new BlockingWaitStrategy();
        const int waiterCount = 5;
        var tasks = new Task<long>[waiterCount];

        // Act
        for (int i = 0; i < waiterCount; i++)
        {
            int sequence = i;
            tasks[i] = StartWaiter(strategy, sequence);
        }

        var results = await SignalUntilCompletedAsync(strategy, Task.WhenAll(tasks));

        // Assert
        for (int i = 0; i < waiterCount; i++)
        {
            Assert.Equal(i, results[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(long.MaxValue)]
    public void AllStrategies_WaitFor_ReturnsInputSequence(long sequence)
    {
        // Arrange
        var strategies = new IWaitStrategy[]
        {
            new SleepingWaitStrategy(),
            new YieldingWaitStrategy()
        };

        foreach (var strategy in strategies)
        {
            // Act
            var result = strategy.WaitFor(sequence);

            // Assert
            Assert.Equal(sequence, result);
        }
    }

    [Fact]
    public async Task SleepingWaitStrategy_ConcurrentCalls_ReturnEachSequence()
    {
        var strategy = new SleepingWaitStrategy();
        var tasks = Enumerable.Range(0, 5)
            .Select(sequence => Task.Run(() => strategy.WaitFor(sequence), TestContext.Current.CancellationToken));

        long[] expected = [0, 1, 2, 3, 4];
        Assert.Equal(expected, await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task YieldingWaitStrategy_ConcurrentCalls_ReturnEachSequence()
    {
        var strategy = new YieldingWaitStrategy();
        var tasks = Enumerable.Range(0, 5)
            .Select(sequence => Task.Run(() => strategy.WaitFor(sequence), TestContext.Current.CancellationToken));

        long[] expected = [0, 1, 2, 3, 4];
        Assert.Equal(expected, await Task.WhenAll(tasks));
    }

    private static Task<long> StartWaiter(IWaitStrategy strategy, long sequence) =>
        Task.Factory.StartNew(
            () => strategy.WaitFor(sequence),
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static async Task<T> SignalUntilCompletedAsync<T>(IWaitStrategy strategy, Task<T> waitTask)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!waitTask.IsCompleted)
        {
            strategy.SignalAllWhenBlocking();
            await Task.Delay(10, timeout.Token);
        }

        return await waitTask;
    }
}
