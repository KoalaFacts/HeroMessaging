using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Processing;

/// <summary>
/// Default implementation of queue processor metrics.
/// </summary>
internal sealed class QueueProcessorMetrics : IQueueProcessorMetrics
{
    /// <summary>
    /// Gets or sets the total number of messages enqueued.
    /// </summary>
    public long TotalMessages { get; init; }

    /// <summary>
    /// Gets or sets the number of successfully processed messages.
    /// </summary>
    public long ProcessedMessages { get; init; }

    /// <summary>
    /// Gets or sets the number of failed messages.
    /// </summary>
    public long FailedMessages { get; init; }
}
