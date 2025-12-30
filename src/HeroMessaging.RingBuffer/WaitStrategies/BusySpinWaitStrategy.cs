namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Busy spin wait strategy that never yields or sleeps.
/// Provides ultra-low latency (less than 10μs) but uses 100% CPU.
/// Best for low-latency trading systems or real-time applications
/// where minimizing latency is critical and CPU usage is not a concern.
/// </summary>
public sealed class BusySpinWaitStrategy : IWaitStrategy
{
    /// <summary>
    /// Busy spin briefly and return - caller loops until sequence is available.
    /// WARNING: This will consume 100% of a CPU core when used in a tight loop.
    /// </summary>
    public long WaitFor(long sequence)
    {
        // Minimal spin - caller loops until sequence is available.
        // This provides ultra-low latency by avoiding any yielding or sleeping.
        Thread.SpinWait(1);
        return sequence;
    }

    /// <summary>
    /// No-op - busy spin doesn't need signaling
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        // No-op
    }
}
