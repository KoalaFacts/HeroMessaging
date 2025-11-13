namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Wait strategy that spins briefly then yields to other threads.
/// Provides low latency (~50-100μs) with moderate CPU usage.
/// Best for scenarios where low latency is important but 100% CPU usage is not acceptable.
/// </summary>
public sealed class YieldingWaitStrategy : IWaitStrategy
{
    private const int SpinTries = 100;

    /// <summary>
    /// Wait by spinning then yielding.
    /// Does not sleep, so consumes more CPU than Sleeping strategy
    /// but provides lower latency.
    /// </summary>
    public long WaitFor(long sequence)
    {
        int counter = SpinTries;

        // Busy spin for a short time
        while (counter > 0)
        {
            counter--;
            Thread.SpinWait(1);
        }

        // Yield CPU to other threads
        Thread.Yield();

        return sequence;
    }

    /// <summary>
    /// No-op - yielding strategy doesn't need signaling
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        // No-op
    }
}
