namespace HeroMessaging.RingBuffer.EventHandlers;

/// <summary>
/// Handler for processing events from the ring buffer.
/// Implement this interface to define custom event processing logic.
/// </summary>
/// <typeparam name="T">The type of event to handle</typeparam>
public interface IEventHandler<in T>
{
    /// <summary>
    /// Called for each event in the ring buffer.
    /// </summary>
    /// <param name="data">The event data to process</param>
    /// <param name="sequence">The sequence number of this event</param>
    /// <param name="endOfBatch">True if this is the last event in the current batch</param>
    void OnEvent(T data, long sequence, bool endOfBatch);

    /// <summary>
    /// Called when an exception occurs during event processing.
    /// Allows for custom error handling logic.
    /// </summary>
    /// <param name="ex">The exception that occurred</param>
    void OnError(Exception ex);

    /// <summary>
    /// Called when the event processor is shutting down.
    /// Use this for cleanup logic.
    /// </summary>
    void OnShutdown();
}
