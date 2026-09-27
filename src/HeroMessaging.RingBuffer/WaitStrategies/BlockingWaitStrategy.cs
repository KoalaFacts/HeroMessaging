namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Blocking wait strategy using Monitor.Wait.
/// Provides the lowest CPU usage at the cost of higher latency (~1-5ms).
/// Best for scenarios where CPU efficiency is more important than latency.
/// </summary>
public sealed class BlockingWaitStrategy : IWaitStrategy, IBackpressureWaitStrategy
{
    private static readonly TimeSpan CapacityCheckInterval = TimeSpan.FromMilliseconds(10);
    private readonly object _lock = new();
    private long _signalVersion;

    /// <summary>
    /// Wait for the sequence using Monitor.Wait (OS-level blocking).
    /// Thread will sleep until signalled by a producer.
    /// </summary>
    public long WaitFor(long sequence)
    {
        lock (_lock)
        {
            var observedVersion = _signalVersion;
            while (_signalVersion == observedVersion)
            {
                Monitor.Wait(_lock);
            }
        }
        return sequence;
    }

    void IBackpressureWaitStrategy.WaitForCapacity(Func<bool> hasCapacity)
    {
        lock (_lock)
        {
            while (!hasCapacity())
            {
                // Consumer sequences can advance without signalling this monitor.
                Monitor.Wait(_lock, CapacityCheckInterval);
            }
        }
    }

    /// <summary>
    /// Wake up all waiting threads
    /// </summary>
    public void SignalAllWhenBlocking()
    {
        lock (_lock)
        {
            _signalVersion++;
            Monitor.PulseAll(_lock);
        }
    }
}
