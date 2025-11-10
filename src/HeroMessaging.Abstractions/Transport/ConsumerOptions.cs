namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Options for configuring message consumers
/// </summary>
public record ConsumerOptions
{
    /// <summary>
    /// Consumer identifier (auto-generated if not specified)
    /// </summary>
    public string? ConsumerId { get; init; }

    /// <summary>
    /// Consumer group name (for competing consumers)
    /// </summary>
    public string? ConsumerGroup { get; init; }

    /// <summary>
    /// Maximum number of concurrent messages to process
    /// </summary>
    public int ConcurrentMessageLimit { get; init; } = 1;

    /// <summary>
    /// Prefetch count (number of messages to fetch ahead)
    /// </summary>
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    /// Whether to automatically acknowledge messages after successful processing
    /// </summary>
    public bool AutoAcknowledge { get; init; } = true;

    /// <summary>
    /// Whether to requeue messages on failure
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;

    /// <summary>
    /// Retry policy for failed message processing
    /// </summary>
    public RetryPolicy MessageRetryPolicy { get; init; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(5),
        MaxDelay = TimeSpan.FromMinutes(1),
        UseExponentialBackoff = true
    };

    /// <summary>
    /// Message lock duration (for brokers that support message locking)
    /// </summary>
    public TimeSpan? MessageLockDuration { get; init; }

    /// <summary>
    /// Whether to enable batching
    /// </summary>
    public bool EnableBatching { get; init; }

    /// <summary>
    /// Maximum batch size
    /// </summary>
    public int MaxBatchSize { get; init; } = 100;

    /// <summary>
    /// Batch timeout
    /// </summary>
    public TimeSpan BatchTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to start consuming immediately
    /// </summary>
    public bool StartImmediately { get; init; } = true;

    /// <summary>
    /// Custom properties for transport-specific configuration
    /// </summary>
    public Dictionary<string, object>? Properties { get; init; }

    /// <summary>
    /// Create default consumer options
    /// </summary>
    public static ConsumerOptions Default => new();

    /// <summary>
    /// Create consumer options for competing consumers
    /// </summary>
    public static ConsumerOptions CompetingConsumer(string consumerGroup, int concurrentMessageLimit = 1)
    {
        return new ConsumerOptions
        {
            ConsumerGroup = consumerGroup,
            ConcurrentMessageLimit = concurrentMessageLimit,
            AutoAcknowledge = true,
            RequeueOnFailure = false
        };
    }

    /// <summary>
    /// Create consumer options for high throughput scenarios
    /// </summary>
    public static ConsumerOptions HighThroughput(int concurrentMessageLimit = 10, ushort prefetchCount = 100)
    {
        return new ConsumerOptions
        {
            ConcurrentMessageLimit = concurrentMessageLimit,
            PrefetchCount = prefetchCount,
            AutoAcknowledge = true,
            EnableBatching = true,
            MaxBatchSize = 100
        };
    }

    /// <summary>
    /// Create consumer options for reliable processing
    /// </summary>
    public static ConsumerOptions Reliable(int maxRetries = 5)
    {
        return new ConsumerOptions
        {
            ConcurrentMessageLimit = 1,
            PrefetchCount = 1,
            AutoAcknowledge = false,
            RequeueOnFailure = true,
            MessageRetryPolicy = new RetryPolicy
            {
                MaxAttempts = maxRetries,
                InitialDelay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromMinutes(1),
                UseExponentialBackoff = true
            }
        };
    }
}

/// <summary>
/// Consumer metrics
/// </summary>
public class ConsumerMetrics : ComponentMetricsBase
{
    /// <summary>
    /// Total messages received
    /// </summary>
    public long MessagesReceived { get; set; }

    /// <summary>
    /// Total messages processed successfully (alias for SuccessfulOperations)
    /// </summary>
    public long MessagesProcessed
    {
        get => SuccessfulOperations;
        set => SuccessfulOperations = value;
    }

    /// <summary>
    /// Total messages failed (alias for FailedOperations)
    /// </summary>
    public long MessagesFailed
    {
        get => FailedOperations;
        set => FailedOperations = value;
    }

    /// <summary>
    /// Total messages acknowledged
    /// </summary>
    public long MessagesAcknowledged { get; set; }

    /// <summary>
    /// Total messages rejected
    /// </summary>
    public long MessagesRejected { get; set; }

    /// <summary>
    /// Total messages dead lettered
    /// </summary>
    public long MessagesDeadLettered { get; set; }

    /// <summary>
    /// Average processing duration
    /// </summary>
    public TimeSpan AverageProcessingDuration { get; set; }

    /// <summary>
    /// Last message received timestamp
    /// </summary>
    public DateTime? LastMessageReceived { get; set; }

    /// <summary>
    /// Last message processed timestamp
    /// </summary>
    public DateTime? LastMessageProcessed { get; set; }

    /// <summary>
    /// Currently processing message count
    /// </summary>
    public int CurrentlyProcessing { get; set; }
}
