namespace HeroMessaging.Processing;

/// <summary>
/// Shared constants for message processing components.
/// Consolidates duplicate constant definitions from CommandProcessor, QueryProcessor, QueueWorker, EventBus.
/// </summary>
public static class ProcessingConstants
{
    /// <summary>
    /// Default bounded capacity for processing queues (CommandProcessor, QueryProcessor, QueueWorker).
    /// </summary>
    public const int DefaultBoundedCapacity = 100;

    /// <summary>
    /// Higher bounded capacity for event buses with parallel processing.
    /// </summary>
    public const int EventBusBoundedCapacity = 1000;

    /// <summary>
    /// Default size for metrics history buffers.
    /// </summary>
    public const int DefaultMetricsHistorySize = 100;

    /// <summary>
    /// Maximum number of times to requeue a failed message before sending to DLQ.
    /// </summary>
    public const int MaxRequeueAttempts = 3;

    /// <summary>
    /// Delay between polls when queue is empty (milliseconds).
    /// </summary>
    public const int EmptyQueuePollDelayMs = 100;

    /// <summary>
    /// Delay after an error before retrying (milliseconds).
    /// </summary>
    public const int ErrorRecoveryDelayMs = 1000;
}
