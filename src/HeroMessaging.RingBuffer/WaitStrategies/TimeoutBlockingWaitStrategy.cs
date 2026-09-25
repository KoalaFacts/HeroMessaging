using System.Diagnostics;

namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Blocking wait strategy with timeout support.
/// Throws TimeoutException if the wait exceeds the specified timeout.
/// Best for scenarios where deadlock detection is needed or
/// you want to fail fast if events aren't being processed.
/// </summary>
public sealed class TimeoutBlockingWaitStrategy : IWaitStrategy
{
    private readonly TimeSpan _timeout;
    private readonly object _lock = new();
    private long _signalVersion;

    /// <summary>
    /// Creates a new timeout blocking wait strategy
    /// </summary>
    /// <param name="timeout">Maximum time to wait before throwing TimeoutException</param>
    public TimeoutBlockingWaitStrategy(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// Wait for the sequence with a timeout.
    /// Throws TimeoutException if the timeout is exceeded.
    /// </summary>
    public long WaitFor(long sequence)
    {
        lock (_lock)
        {
            var observedVersion = _signalVersion;
            var startedAt = Stopwatch.GetTimestamp();
            while (_signalVersion == observedVersion)
            {
                var remaining = _timeout - Stopwatch.GetElapsedTime(startedAt);
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_lock, remaining))
                {
                    throw new TimeoutException(
                        $"Timeout waiting for sequence {sequence} after {_timeout}");
                }
            }
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
            _signalVersion++;
            Monitor.PulseAll(_lock);
        }
    }
}
