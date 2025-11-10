namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Configuration options for batch message processing
/// </summary>
/// <remarks>
/// <para>
/// Configures batch accumulation behavior, size limits, and timeout settings for optimal
/// throughput while maintaining low latency requirements.
/// </para>
/// <para>
/// <strong>Performance Trade-offs</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Larger batch sizes improve throughput but increase latency</description></item>
/// <item><description>Shorter timeouts reduce latency but may result in smaller batches</description></item>
/// <item><description>Optimal settings depend on message arrival patterns and workload characteristics</description></item>
/// </list>
/// </remarks>
public sealed class BatchProcessingOptions
{
    /// <summary>
    /// Gets or sets whether batch processing is enabled
    /// </summary>
    /// <remarks>
    /// When disabled, messages are processed individually through the standard pipeline.
    /// Default: false
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of messages to accumulate before processing a batch
    /// </summary>
    /// <remarks>
    /// When this limit is reached, the batch is processed immediately regardless of timeout.
    /// Recommended: 10-100 for most workloads, 100-1000 for high-throughput scenarios.
    /// Default: 50
    /// </remarks>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum time to wait for messages before processing a partial batch
    /// </summary>
    /// <remarks>
    /// This ensures low latency even when message arrival rate is low.
    /// After this timeout, any accumulated messages are processed even if below MaxBatchSize.
    /// Recommended: 100ms-1000ms depending on latency requirements.
    /// Default: 200ms
    /// </remarks>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the minimum batch size before timeout-based processing is triggered
    /// </summary>
    /// <remarks>
    /// This prevents processing of very small batches that don't justify the overhead.
    /// If fewer messages than this are accumulated when timeout occurs, they are processed individually.
    /// Default: 2
    /// </remarks>
    public int MinBatchSize { get; set; } = 2;

    /// <summary>
    /// Gets or sets whether to process messages individually if batch processing fails
    /// </summary>
    /// <remarks>
    /// When true, if batch processing throws an exception, each message in the batch
    /// is reprocessed individually to identify which specific messages failed.
    /// This provides better error isolation but increases processing time on failure.
    /// Default: true
    /// </remarks>
    public bool FallbackToIndividualProcessing { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for processing messages within a batch
    /// </summary>
    /// <remarks>
    /// Controls how many messages from a batch can be processed concurrently.
    /// Set to 1 for sequential processing, higher values for parallel processing.
    /// Note: Parallel processing may impact ordering guarantees.
    /// Default: 1 (sequential)
    /// </remarks>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to continue processing remaining messages if some fail
    /// </summary>
    /// <remarks>
    /// When true, if a message fails during batch processing, the remaining messages
    /// in the batch continue to be processed.
    /// When false, batch processing stops at the first failure.
    /// Default: true
    /// </remarks>
    public bool ContinueOnFailure { get; set; } = true;

    /// <summary>
    /// Validates the configuration options
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    public void Validate()
    {
        if (MaxBatchSize <= 0)
            throw new ArgumentException("MaxBatchSize must be greater than 0", nameof(MaxBatchSize));

        if (BatchTimeout <= TimeSpan.Zero)
            throw new ArgumentException("BatchTimeout must be greater than zero", nameof(BatchTimeout));

        if (MinBatchSize < 1)
            throw new ArgumentException("MinBatchSize must be at least 1", nameof(MinBatchSize));

        if (MinBatchSize > MaxBatchSize)
            throw new ArgumentException("MinBatchSize cannot be greater than MaxBatchSize", nameof(MinBatchSize));

        if (MaxDegreeOfParallelism <= 0)
            throw new ArgumentException("MaxDegreeOfParallelism must be greater than 0", nameof(MaxDegreeOfParallelism));
    }
}
