using HeroMessaging.RingBuffer.EventProcessors;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// Associates an InMemoryConsumer with its event processor for lifecycle management.
/// </summary>
internal sealed class ConsumerProcessor : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumerProcessor"/> class.
    /// </summary>
    /// <param name="consumer">The consumer to process messages.</param>
    /// <param name="processor">The event processor (may be null for shared processor mode).</param>
    public ConsumerProcessor(InMemoryConsumer consumer, IEventProcessor? processor)
    {
        Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        Processor = processor;
    }

    /// <summary>
    /// Gets the consumer that receives messages.
    /// </summary>
    public InMemoryConsumer Consumer { get; }

    /// <summary>
    /// Gets the event processor responsible for processing events.
    /// </summary>
    public IEventProcessor? Processor { get; }

    /// <summary>
    /// Disposes the processor if present.
    /// </summary>
    public void Dispose()
    {
        if (Processor != null)
        {
            Processor.Stop();
            Processor.Dispose();
        }
    }
}
