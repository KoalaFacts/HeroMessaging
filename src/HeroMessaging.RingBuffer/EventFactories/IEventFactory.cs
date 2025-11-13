namespace HeroMessaging.RingBuffer.EventFactories;

/// <summary>
/// Factory for creating pre-allocated events in the ring buffer.
/// The ring buffer pre-allocates all slots to avoid allocation overhead during publishing.
/// </summary>
/// <typeparam name="T">The type of event to create</typeparam>
public interface IEventFactory<T> where T : class
{
    /// <summary>
    /// Creates a new instance of the event.
    /// Called once for each slot in the ring buffer during initialization.
    /// </summary>
    /// <returns>A new event instance</returns>
    T Create();
}
