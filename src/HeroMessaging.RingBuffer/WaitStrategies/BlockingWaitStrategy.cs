namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Blocking wait strategy using Monitor.Wait.
/// Provides the lowest CPU usage at the cost of higher latency (~1-5ms).
/// Best for scenarios where CPU efficiency is more important than latency.
/// </summary>
public sealed class BlockingWaitStrategy : IWaitStrategy
{
    private readonly object _lock = new();
    private volatile bool _signalled;

    /// <summary>
    /// Wait for the sequence using Monitor.Wait (OS-level blocking).
    /// Thread will sleep until signalled by a producer.
    /// </summary>
    public long WaitFor(long sequence)
    {
        lock (_lock)
        {
            while (!_signalled)
            {
                Monitor.Wait(_lock);
            }
            _signalled = false;
        }
        return sequence;
    }

    /// <summary>
    /// Wake up all waiting threads
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        lock (_lock)
        {
            _signalled = true;
            Monitor.PulseAll(_lock);
        }
    }
}
