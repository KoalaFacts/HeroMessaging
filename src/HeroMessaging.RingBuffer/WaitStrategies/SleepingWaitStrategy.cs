namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Progressive back-off wait strategy: spin, yield, then sleep.
/// Provides a balance between CPU usage and latency (~100μs-1ms).
/// Best for general-purpose use where both latency and CPU efficiency matter.
/// </summary>
public sealed class SleepingWaitStrategy : IWaitStrategy
{
    private const int SpinTries = 100;
    private const int YieldTries = 100;

    /// <summary>
    /// Wait using progressive back-off:
    /// 1. Busy spin for a short time (low latency)
    /// 2. Yield to other threads (medium latency)
    /// 3. Sleep for 1ms (low CPU, higher latency)
    /// </summary>
    public long WaitFor(long sequence)
    {
        int counter = SpinTries + YieldTries;

        // Phase 1: Busy spin
        while (counter > YieldTries)
        {
            counter--;
            Thread.SpinWait(1);
        }

        // Phase 2: Yield to other threads
        while (counter > 0)
        {
            counter--;
            Thread.Yield();
        }

        // Phase 3: Sleep to avoid wasting CPU
        Thread.Sleep(1);

        return sequence;
    }

    /// <summary>
    /// No-op - sleeping strategy doesn't need signaling
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        // No-op for sleeping strategy
    }
}
