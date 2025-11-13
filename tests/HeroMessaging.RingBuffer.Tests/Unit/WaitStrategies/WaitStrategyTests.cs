using HeroMessaging.RingBuffer.WaitStrategies;
using System.Diagnostics;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.WaitStrategies;

[Trait("Category", "Unit")]
public class WaitStrategyTests
{
    [Fact]
    public void BlockingWaitStrategy_WaitFor_ReturnsSequence()
    {
        // Arrange
        var strategy = new BlockingWaitStrategy();
        const long expectedSequence = 42;

        // Act - Signal in parallel
        var waitTask = Task.Run(() => strategy.WaitFor(expectedSequence));
        Task.Delay(10).ContinueWith(_ => strategy.SignalAllWhenBlocking());

        var result = waitTask.Result;

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
    public void TimeoutBlockingWaitStrategy_WaitFor_WithSignal_ReturnsSequence()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(1);
        var strategy = new TimeoutBlockingWaitStrategy(timeout);
        const long expectedSequence = 42;

        // Act - Signal in parallel
        var waitTask = Task.Run(() => strategy.WaitFor(expectedSequence));
        Task.Delay(10).ContinueWith(_ => strategy.SignalAllWhenBlocking());

        var result = waitTask.Result;

        // Assert
        Assert.Equal(expectedSequence, result);
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
    public void BlockingWaitStrategy_MultipleWaiters_AllSignaled()
    {
        // Arrange
        var strategy = new BlockingWaitStrategy();
        const int waiterCount = 5;
        var tasks = new Task<long>[waiterCount];

        // Act
        for (int i = 0; i < waiterCount; i++)
        {
            int sequence = i;
            tasks[i] = Task.Run(() => strategy.WaitFor(sequence));
        }

        Task.Delay(10).Wait();
        strategy.SignalAllWhenBlocking();

        Task.WaitAll(tasks);

        // Assert
        for (int i = 0; i < waiterCount; i++)
        {
            Assert.Equal(i, tasks[i].Result);
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
    public void SleepingWaitStrategy_HasReasonableLatency()
    {
        // Arrange
        var strategy = new SleepingWaitStrategy();
        var sw = Stopwatch.StartNew();

        // Act
        strategy.WaitFor(0);
        sw.Stop();

        // Assert - Should complete quickly (spin + yield + sleep)
        // This is approximate - depends on system load
        Assert.True(sw.ElapsedMilliseconds < 100,
            $"SleepingWaitStrategy took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void YieldingWaitStrategy_HasLowerLatencyThanSleeping()
    {
        // Arrange
        var sleeping = new SleepingWaitStrategy();
        var yielding = new YieldingWaitStrategy();

        // Act
        var swSleeping = Stopwatch.StartNew();
        sleeping.WaitFor(0);
        swSleeping.Stop();

        var swYielding = Stopwatch.StartNew();
        yielding.WaitFor(0);
        swYielding.Stop();

        // Assert - Yielding should be faster (no sleep)
        Assert.True(swYielding.ElapsedMilliseconds <= swSleeping.ElapsedMilliseconds,
            $"Yielding: {swYielding.ElapsedMilliseconds}ms, Sleeping: {swSleeping.ElapsedMilliseconds}ms");
    }
}
