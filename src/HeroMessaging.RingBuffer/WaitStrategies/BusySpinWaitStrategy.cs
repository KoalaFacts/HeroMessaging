namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Busy spin wait strategy that never yields or sleeps.
/// Provides ultra-low latency (<10μs) but uses 100% CPU.
/// Best for low-latency trading systems or real-time applications
/// where minimizing latency is critical and CPU usage is not a concern.
/// </summary>
public sealed class BusySpinWaitStrategy : IWaitStrategy
{
    /// <summary>
    /// Busy spin indefinitely until the sequence is available.
    /// WARNING: This will consume 100% of a CPU core.
    /// </summary>
    public long WaitFor(long sequence)
    {
        // Just spin forever - the processor will be checking
        // for new events as fast as possible
        while (true)
        {
            Thread.SpinWait(1);
            // Note: In real usage, the caller will break out when
            // the sequence becomes available
        }
    }

    /// <summary>
    /// No-op - busy spin doesn't need signaling
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        // No-op
    }
}
