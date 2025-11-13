namespace HeroMessaging.RingBuffer.WaitStrategies;

/// <summary>
/// Strategy for waiting on new events in the ring buffer.
/// Different strategies trade off between CPU usage and latency.
/// </summary>
public interface IWaitStrategy
{
    /// <summary>
    /// Wait for the given sequence to become available.
    /// This method is called by consumers waiting for new events.
    /// </summary>
    /// <param name="sequence">The sequence to wait for</param>
    /// <returns>The available sequence number (typically the same as input)</returns>
    long WaitFor(long sequence);

    /// <summary>
    /// Signal all waiting consumers that new events are available.
    /// Called by producers after publishing events.
    /// </summary>
    void SignalAllWhenBlocking();
}
