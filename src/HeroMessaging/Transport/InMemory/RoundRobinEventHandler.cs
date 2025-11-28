using HeroMessaging.RingBuffer.EventHandlers;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// Event handler that delivers messages in round-robin fashion to all consumers.
/// </summary>
internal sealed class RoundRobinEventHandler : IEventHandler<MessageEvent>
{
    private readonly RingBufferQueue _queue;
    private int _consumerIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoundRobinEventHandler"/> class.
    /// </summary>
    /// <param name="queue">The parent queue that owns the consumers.</param>
    public RoundRobinEventHandler(RingBufferQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>
    /// Handles an incoming event by distributing it to consumers.
    /// </summary>
    /// <param name="evt">The message event to process.</param>
    /// <param name="sequence">The sequence number of the event.</param>
    /// <param name="endOfBatch">Whether this is the last event in the current batch.</param>
    public void OnEvent(MessageEvent evt, long sequence, bool endOfBatch)
    {
        if (evt.Envelope.HasValue)
        {
            try
            {
                // Get current consumers list (thread-safe copy)
                var consumers = _queue.GetConsumersSnapshot();

                if (consumers != null && consumers.Length > 0)
                {
                    // Round-robin distribution
                    var index = unchecked((uint)Interlocked.Increment(ref _consumerIndex));
                    var consumer = consumers[index % (uint)consumers.Length];

                    // Deliver message to selected consumer
                    consumer.DeliverMessageAsync(evt.Envelope.Value, default).GetAwaiter().GetResult();
                }

                // Decrement depth counter
                _queue.DecrementDepth();
            }
            catch (Exception)
            {
                // Log error - in production would use ILogger
                // For now, just decrement depth and continue
                _queue.DecrementDepth();
            }
            finally
            {
                // Clear envelope for reuse (zero allocation)
                evt.Envelope = null;
            }
        }
    }

    /// <summary>
    /// Called when an error occurs during event processing.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    public void OnError(Exception ex)
    {
        // Log error - in production would use ILogger
    }

    /// <summary>
    /// Called when the event processor is shutting down.
    /// </summary>
    public void OnShutdown()
    {
        // Cleanup - nothing to do currently
    }
}
