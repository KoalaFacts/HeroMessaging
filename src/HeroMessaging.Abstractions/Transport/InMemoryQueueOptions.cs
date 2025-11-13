namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Configuration options for in-memory queues.
/// </summary>
public class InMemoryQueueOptions
{
    /// <summary>
    /// Queue implementation mode.
    /// Default is Channel (existing async/await implementation).
    /// </summary>
    public QueueMode Mode { get; set; } = QueueMode.Channel;

    /// <summary>
    /// Maximum queue buffer size.
    /// For RingBuffer mode, this must be a power of 2 (e.g., 256, 512, 1024, 2048, 4096).
    /// Default: 1024
    /// </summary>
    public int BufferSize { get; set; } = 1024;

    /// <summary>
    /// Drop oldest messages when buffer is full.
    /// Only applies to Channel mode.
    /// Default: false (block when full)
    /// </summary>
    public bool DropWhenFull { get; set; } = false;

    /// <summary>
    /// Wait strategy for consumers.
    /// Only applies to RingBuffer mode.
    /// Default: Sleeping (balanced CPU/latency)
    /// </summary>
    public WaitStrategy WaitStrategy { get; set; } = WaitStrategy.Sleeping;

    /// <summary>
    /// Producer type for coordination.
    /// Only applies to RingBuffer mode.
    /// Default: Multi (supports multiple publishers)
    /// </summary>
    public ProducerMode ProducerMode { get; set; } = ProducerMode.Multi;

    /// <summary>
    /// Validate the options are correct.
    /// </summary>
    public void Validate()
    {
        if (BufferSize <= 0)
        {
            throw new ArgumentException("BufferSize must be positive", nameof(BufferSize));
        }

        if (Mode == QueueMode.RingBuffer && !IsPowerOf2(BufferSize))
        {
            throw new ArgumentException(
                $"BufferSize must be power of 2 when using RingBuffer mode. " +
                $"Valid values: 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, etc. " +
                $"Current value: {BufferSize}",
                nameof(BufferSize));
        }
    }

    private static bool IsPowerOf2(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
