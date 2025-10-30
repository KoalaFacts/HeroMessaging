namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Provides basic metrics about message processing operations.
/// </summary>
/// <remarks>
/// This interface is implemented by command and event processors to expose
/// operational metrics for monitoring, alerting, and diagnostics.
///
/// Metrics are cumulative counters and calculated averages since the processor started.
/// Use these metrics to:
/// - Monitor processing throughput and error rates
/// - Set up alerts for failures or performance degradation
/// - Track SLA compliance
/// - Identify performance bottlenecks
///
/// <code>
/// var metrics = processor.GetMetrics();
/// if (metrics.FailedCount > 0)
/// {
///     var errorRate = (double)metrics.FailedCount / metrics.ProcessedCount;
///     if (errorRate > 0.05) // 5% error threshold
///     {
///         logger.LogWarning("High error rate detected: {ErrorRate:P}", errorRate);
///     }
/// }
/// </code>
/// </remarks>
public interface IProcessorMetrics
{
    /// <summary>
    /// Gets the total number of messages successfully processed since the processor started.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter that increases with each successful processing operation.
    /// Does not include failed attempts.
    ///
    /// Use this to:
    /// - Calculate throughput (messages per second)
    /// - Monitor processing volume trends
    /// - Validate that processing is occurring
    /// </remarks>
    long ProcessedCount { get; }

    /// <summary>
    /// Gets the total number of messages that failed processing since the processor started.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter that increases with each processing failure.
    /// Includes both retryable and permanent failures.
    ///
    /// Use this to:
    /// - Calculate error rate (FailedCount / (ProcessedCount + FailedCount))
    /// - Set up alerting thresholds
    /// - Identify problematic message types
    /// - Monitor system health
    /// </remarks>
    long FailedCount { get; }

    /// <summary>
    /// Gets the average duration of message processing operations.
    /// </summary>
    /// <remarks>
    /// This is a rolling average across all processed messages (successful and failed).
    /// Typically calculated using exponential moving average or simple moving average.
    ///
    /// Use this to:
    /// - Monitor processing performance trends
    /// - Detect performance degradation
    /// - Identify slow message types
    /// - Validate SLA compliance (target: &lt;1ms framework overhead)
    ///
    /// Note: This represents framework processing time, which may not include
    /// business logic execution time depending on the implementation.
    /// </remarks>
    TimeSpan AverageDuration { get; }
}

/// <summary>
/// Provides metrics specific to event bus operations.
/// </summary>
/// <remarks>
/// Event bus metrics track event publishing, handler registration, and failure rates.
/// Unlike command/query processors, events can have multiple handlers (0 to N).
///
/// Use these metrics to:
/// - Monitor event publishing volume
/// - Track handler registration status
/// - Detect event handler failures
/// - Validate event-driven architecture health
///
/// <code>
/// var eventMetrics = eventBus.GetMetrics();
/// logger.LogInformation(
///     "Event Bus: Published={Published}, Failed={Failed}, Handlers={Handlers}",
///     eventMetrics.PublishedCount,
///     eventMetrics.FailedCount,
///     eventMetrics.RegisteredHandlers
/// );
/// </code>
/// </remarks>
public interface IEventBusMetrics
{
    /// <summary>
    /// Gets the total number of events published through the event bus.
    /// </summary>
    /// <remarks>
    /// This count includes all events regardless of whether any handlers were registered.
    /// An event is counted when published, not when handlers complete processing.
    ///
    /// Use this to:
    /// - Monitor event publishing volume
    /// - Track event-driven architecture activity
    /// - Calculate event publishing rate
    /// </remarks>
    long PublishedCount { get; }

    /// <summary>
    /// Gets the total number of event publishing operations that failed.
    /// </summary>
    /// <remarks>
    /// A failure is counted when the event publishing infrastructure fails,
    /// not when individual handlers fail. Handler-level failures are tracked separately.
    ///
    /// Use this to:
    /// - Monitor event bus reliability
    /// - Set up infrastructure failure alerts
    /// - Detect transport or serialization issues
    /// </remarks>
    long FailedCount { get; }

    /// <summary>
    /// Gets the current number of event handlers registered across all event types.
    /// </summary>
    /// <remarks>
    /// This is a point-in-time count of all active event handler registrations.
    /// Each event type may have 0 to N handlers.
    ///
    /// Use this to:
    /// - Verify handlers are registered
    /// - Detect missing handler registrations
    /// - Monitor handler lifecycle (registrations/deregistrations)
    /// - Validate system configuration
    ///
    /// Example: If you have 3 handlers for OrderCreatedEvent and 2 for CustomerRegisteredEvent,
    /// this property returns 5.
    /// </remarks>
    int RegisteredHandlers { get; }
}

/// <summary>
/// Provides metrics specific to query processing, including cache performance.
/// </summary>
/// <remarks>
/// Query processors extend basic processor metrics with cache hit rate tracking.
/// Queries are read-only operations that should be idempotent and cacheable.
///
/// Use these metrics to:
/// - Monitor query caching effectiveness
/// - Optimize cache configuration
/// - Reduce database load
/// - Improve response times
///
/// <code>
/// var queryMetrics = queryProcessor.GetMetrics();
/// if (queryMetrics.CacheHitRate &lt; 0.70) // Below 70% hit rate
/// {
///     logger.LogWarning(
///         "Low cache hit rate: {HitRate:P}. Consider increasing cache size or TTL.",
///         queryMetrics.CacheHitRate
///     );
/// }
/// </code>
/// </remarks>
public interface IQueryProcessorMetrics : IProcessorMetrics
{
    /// <summary>
    /// Gets the cache hit rate as a value between 0.0 (0%) and 1.0 (100%).
    /// </summary>
    /// <remarks>
    /// Cache hit rate = (Cache Hits) / (Cache Hits + Cache Misses)
    ///
    /// Interpretation:
    /// - 1.0 (100%): All queries served from cache (optimal)
    /// - 0.7-0.9 (70-90%): Good cache effectiveness
    /// - 0.3-0.7 (30-70%): Moderate cache effectiveness
    /// - 0.0-0.3 (0-30%): Poor cache effectiveness, review cache configuration
    ///
    /// Use this to:
    /// - Tune cache size and eviction policies
    /// - Identify frequently accessed queries for optimization
    /// - Measure cache effectiveness after configuration changes
    /// - Reduce database load by improving cache hit rate
    /// </remarks>
    double CacheHitRate { get; }
}

/// <summary>
/// Provides metrics specific to queue processing operations.
/// </summary>
/// <remarks>
/// Queue processors handle background job processing with metrics for queued,
/// processed, and failed messages across all queues.
///
/// Use these metrics to:
/// - Monitor queue processing throughput
/// - Detect queue backlogs
/// - Track failure rates
/// - Optimize worker count and concurrency
///
/// <code>
/// var queueMetrics = queueProcessor.GetMetrics();
/// var pending = queueMetrics.TotalMessages - queueMetrics.ProcessedMessages - queueMetrics.FailedMessages;
///
/// if (pending > 10000)
/// {
///     logger.LogWarning("Queue backlog detected: {Pending} messages pending", pending);
/// }
/// </code>
/// </remarks>
public interface IQueueProcessorMetrics
{
    /// <summary>
    /// Gets the total number of messages ever enqueued across all queues.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of all messages added to queues since the processor started.
    /// Includes messages that are pending, processed, or failed.
    ///
    /// Use this to:
    /// - Calculate queue ingestion rate
    /// - Monitor overall queue usage
    /// - Track message volume trends
    /// </remarks>
    long TotalMessages { get; }

    /// <summary>
    /// Gets the total number of messages successfully processed from all queues.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages that were dequeued and processed successfully.
    ///
    /// Use this to:
    /// - Calculate processing throughput
    /// - Monitor queue drain rate
    /// - Verify workers are processing messages
    /// </remarks>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the total number of messages that failed processing across all queues.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages that failed after all retry attempts.
    /// Typically these messages are moved to a dead-letter queue.
    ///
    /// Use this to:
    /// - Calculate queue failure rate
    /// - Set up failure alerts
    /// - Monitor dead-letter queue growth
    /// - Identify problematic message types
    /// </remarks>
    long FailedMessages { get; }
}

/// <summary>
/// Provides metrics specific to outbox pattern processing.
/// </summary>
/// <remarks>
/// The outbox pattern ensures transactional message publishing by storing messages
/// in a database table within the same transaction as business changes, then
/// publishing them asynchronously via a background processor.
///
/// Use these metrics to:
/// - Monitor outbox processing lag
/// - Detect publishing failures
/// - Ensure messages are being published
/// - Track outbox table growth
///
/// <code>
/// var outboxMetrics = outboxProcessor.GetMetrics();
///
/// if (outboxMetrics.PendingMessages > 1000)
/// {
///     logger.LogWarning("Outbox backlog: {Pending} messages pending", outboxMetrics.PendingMessages);
/// }
///
/// if (outboxMetrics.LastProcessedTime &lt; DateTime.UtcNow.AddMinutes(-5))
/// {
///     logger.LogError("Outbox processor appears stuck. Last processed: {LastProcessed}",
///         outboxMetrics.LastProcessedTime);
/// }
/// </code>
/// </remarks>
public interface IOutboxProcessorMetrics
{
    /// <summary>
    /// Gets the current number of messages in the outbox waiting to be published.
    /// </summary>
    /// <remarks>
    /// This is a point-in-time count of messages in the outbox table with status = Pending.
    /// Messages are added by business transactions and removed after successful publishing.
    ///
    /// Use this to:
    /// - Monitor outbox lag (should be close to 0 in healthy systems)
    /// - Detect processing bottlenecks
    /// - Set up backlog alerts
    /// - Plan outbox cleanup strategies
    ///
    /// High values indicate:
    /// - Processor is slower than message creation rate
    /// - Processor may be stopped or failing
    /// - Transport or destination may be unavailable
    /// </remarks>
    long PendingMessages { get; }

    /// <summary>
    /// Gets the total number of messages successfully published from the outbox.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages published and marked as completed.
    ///
    /// Use this to:
    /// - Calculate publishing throughput
    /// - Monitor outbox processor activity
    /// - Verify messages are being published
    /// </remarks>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the total number of messages that failed to publish after all retry attempts.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages that failed publishing permanently.
    /// These messages typically require manual intervention or are moved to a dead-letter queue.
    ///
    /// Use this to:
    /// - Calculate failure rate
    /// - Set up failure alerts
    /// - Identify systemic publishing issues
    /// - Monitor dead-letter queue growth
    /// </remarks>
    long FailedMessages { get; }

    /// <summary>
    /// Gets the timestamp when the outbox processor last successfully published a message.
    /// Null if no messages have been processed yet.
    /// </summary>
    /// <remarks>
    /// Use this to:
    /// - Detect stalled processors (compare to current time)
    /// - Monitor processor liveness
    /// - Set up alerting for processing delays
    /// - Diagnose outbox processing issues
    ///
    /// If this value hasn't updated in several minutes and PendingMessages > 0,
    /// the processor may be stuck or stopped.
    /// </remarks>
    DateTime? LastProcessedTime { get; }
}

/// <summary>
/// Provides metrics specific to inbox pattern processing.
/// </summary>
/// <remarks>
/// The inbox pattern ensures exactly-once message processing by deduplicating
/// messages based on MessageId and storing processed message IDs in an inbox table.
///
/// Use these metrics to:
/// - Monitor deduplication effectiveness
/// - Detect duplicate message sources
/// - Validate exactly-once processing
/// - Track inbox table growth
///
/// <code>
/// var inboxMetrics = inboxProcessor.GetMetrics();
///
/// logger.LogInformation(
///     "Inbox: Processed={Processed}, Duplicates={Duplicates}, Dedup Rate={Rate:P}",
///     inboxMetrics.ProcessedMessages,
///     inboxMetrics.DuplicateMessages,
///     inboxMetrics.DeduplicationRate
/// );
///
/// if (inboxMetrics.DeduplicationRate > 0.10) // More than 10% duplicates
/// {
///     logger.LogWarning("High duplicate rate detected. Check message source for duplicate sends.");
/// }
/// </code>
/// </remarks>
public interface IInboxProcessorMetrics
{
    /// <summary>
    /// Gets the total number of unique messages successfully processed through the inbox.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of unique messages (by MessageId) that were
    /// processed exactly once. Duplicate messages are not counted here.
    ///
    /// Use this to:
    /// - Calculate unique message processing throughput
    /// - Monitor inbox processor activity
    /// - Verify exactly-once processing
    /// </remarks>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the total number of duplicate messages detected and skipped.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages that were received but skipped
    /// because they had already been processed (same MessageId exists in inbox).
    ///
    /// Use this to:
    /// - Monitor duplicate message frequency
    /// - Validate deduplication is working
    /// - Identify sources sending duplicates
    /// - Calculate deduplication rate
    ///
    /// Some duplicates are expected in distributed systems with at-least-once
    /// delivery guarantees. Very high rates may indicate:
    /// - Message broker retry configuration issues
    /// - Network problems causing duplicate sends
    /// - Application bugs causing duplicate publishing
    /// </remarks>
    long DuplicateMessages { get; }

    /// <summary>
    /// Gets the total number of messages that failed processing after all retry attempts.
    /// </summary>
    /// <remarks>
    /// This is a cumulative counter of messages that failed permanently.
    /// Failed messages are recorded in the inbox to prevent retry loops,
    /// but may require manual intervention or investigation.
    ///
    /// Use this to:
    /// - Calculate failure rate
    /// - Set up failure alerts
    /// - Identify problematic message types
    /// - Monitor system health
    /// </remarks>
    long FailedMessages { get; }

    /// <summary>
    /// Gets the deduplication rate as a value between 0.0 (0%) and 1.0 (100%).
    /// </summary>
    /// <remarks>
    /// Deduplication rate = (Duplicate Messages) / (Processed Messages + Duplicate Messages)
    ///
    /// Interpretation:
    /// - 0.0 (0%): No duplicates detected (ideal, but unlikely in distributed systems)
    /// - 0.01-0.05 (1-5%): Normal duplication rate in at-least-once delivery systems
    /// - 0.05-0.10 (5-10%): Moderate duplication, review message source configuration
    /// - >0.10 (>10%): High duplication rate, investigate message broker or source
    ///
    /// Use this to:
    /// - Monitor deduplication effectiveness
    /// - Validate inbox pattern is working
    /// - Identify systems sending excessive duplicates
    /// - Tune message broker retry/timeout settings
    /// </remarks>
    double DeduplicationRate { get; }
}

/// <summary>
/// Base interface for all background processors in the messaging system.
/// </summary>
/// <remarks>
/// Processors are long-running background services that continuously process messages.
/// Common processor types include:
/// - Queue processors (process background jobs)
/// - Outbox processors (publish transactional messages)
/// - Inbox processors (deduplicate incoming messages)
///
/// Use this interface to:
/// - Check if processor is running
/// - Implement lifecycle management
/// - Support processor start/stop operations
/// </remarks>
public interface IProcessor
{
    /// <summary>
    /// Gets a value indicating whether the processor is currently running.
    /// </summary>
    /// <remarks>
    /// This property indicates the current operational state:
    /// - true: Processor is running and actively processing messages
    /// - false: Processor is stopped or not yet started
    ///
    /// Use this to:
    /// - Verify processor has started successfully
    /// - Prevent duplicate start operations
    /// - Monitor processor lifecycle
    /// - Implement health checks
    ///
    /// Note: A processor that is running but idle (no messages to process)
    /// will still return true.
    /// </remarks>
    bool IsRunning { get; }
}

/// <summary>
/// Provides operations for managing and monitoring queue processing.
/// </summary>
/// <remarks>
/// Queue processors handle background message processing from named queues.
/// They continuously poll queues for messages and dispatch them to registered handlers.
///
/// Use this interface to:
/// - Get queue processing metrics
/// - Query active queue names
/// - Monitor queue processor status
///
/// <code>
/// var queueProcessor = serviceProvider.GetRequiredService&lt;IQueueProcessor&gt;();
///
/// if (queueProcessor.IsRunning)
/// {
///     var activeQueues = await queueProcessor.GetActiveQueues();
///     var metrics = queueProcessor.GetMetrics();
///
///     foreach (var queue in activeQueues)
///     {
///         logger.LogInformation("Queue {Queue} is active", queue);
///     }
/// }
/// </code>
/// </remarks>
public interface IQueueProcessor : IProcessor
{
    /// <summary>
    /// Gets the current metrics for queue processing operations.
    /// </summary>
    /// <returns>Metrics including total, processed, and failed message counts.</returns>
    /// <remarks>
    /// Returns a snapshot of current metrics across all queues being processed.
    /// Metrics are cumulative since the processor started.
    /// </remarks>
    IQueueProcessorMetrics GetMetrics();

    /// <summary>
    /// Gets the names of all queues that currently have registered handlers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A collection of queue names that are actively being processed.</returns>
    /// <remarks>
    /// This method returns the names of queues that have been started and have
    /// message handlers registered. Empty queues are included if they are started.
    ///
    /// Use this to:
    /// - Verify expected queues are running
    /// - Discover available queues dynamically
    /// - Monitor queue lifecycle
    /// - Implement queue-level health checks
    ///
    /// <code>
    /// var activeQueues = await queueProcessor.GetActiveQueues();
    /// if (!activeQueues.Contains("critical-orders"))
    /// {
    ///     logger.LogError("Critical orders queue is not running!");
    /// }
    /// </code>
    /// </remarks>
    Task<IEnumerable<string>> GetActiveQueues(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides operations for managing and monitoring outbox processing.
/// </summary>
/// <remarks>
/// Outbox processors implement the Transactional Outbox pattern, ensuring that
/// messages are published atomically with database changes by:
/// 1. Storing messages in outbox table within transaction
/// 2. Publishing messages asynchronously via background processor
/// 3. Marking messages as published or failed
///
/// Use this interface to:
/// - Get outbox processing metrics
/// - Monitor outbox processor status
/// - Verify messages are being published
///
/// <code>
/// var outboxProcessor = serviceProvider.GetRequiredService&lt;IOutboxProcessor&gt;();
///
/// if (outboxProcessor.IsRunning)
/// {
///     var metrics = outboxProcessor.GetMetrics();
///     if (metrics.PendingMessages > 100)
///     {
///         logger.LogWarning("Outbox backlog: {Count}", metrics.PendingMessages);
///     }
/// }
/// </code>
/// </remarks>
public interface IOutboxProcessor : IProcessor
{
    /// <summary>
    /// Gets the current metrics for outbox processing operations.
    /// </summary>
    /// <returns>
    /// Metrics including pending, processed, and failed message counts,
    /// plus the timestamp of the last processed message.
    /// </returns>
    /// <remarks>
    /// Returns a snapshot of current outbox state. Use these metrics to:
    /// - Monitor publishing lag (PendingMessages)
    /// - Verify processor is running (LastProcessedTime)
    /// - Track failure rates (FailedMessages)
    /// - Calculate throughput (ProcessedMessages)
    /// </remarks>
    IOutboxProcessorMetrics GetMetrics();
}

/// <summary>
/// Provides operations for managing and monitoring inbox processing.
/// </summary>
/// <remarks>
/// Inbox processors implement the Inbox pattern for exactly-once message processing by:
/// 1. Checking if message has already been processed (by MessageId)
/// 2. If new: storing in inbox, processing message, marking as processed
/// 3. If duplicate: skipping processing (idempotent)
///
/// Use this interface to:
/// - Get inbox processing metrics
/// - Monitor deduplication effectiveness
/// - Verify exactly-once processing
///
/// <code>
/// var inboxProcessor = serviceProvider.GetRequiredService&lt;IInboxProcessor&gt;();
///
/// if (inboxProcessor.IsRunning)
/// {
///     var metrics = inboxProcessor.GetMetrics();
///     logger.LogInformation(
///         "Inbox: {Processed} processed, {Duplicates} duplicates ({Rate:P} dedup rate)",
///         metrics.ProcessedMessages,
///         metrics.DuplicateMessages,
///         metrics.DeduplicationRate
///     );
/// }
/// </code>
/// </remarks>
public interface IInboxProcessor : IProcessor
{
    /// <summary>
    /// Gets the current metrics for inbox processing operations.
    /// </summary>
    /// <returns>
    /// Metrics including processed, duplicate, and failed message counts,
    /// plus the calculated deduplication rate.
    /// </returns>
    /// <remarks>
    /// Returns a snapshot of current inbox state. Use these metrics to:
    /// - Verify exactly-once processing is working (DeduplicationRate)
    /// - Monitor message processing volume (ProcessedMessages)
    /// - Track duplicate frequency (DuplicateMessages)
    /// - Identify failures requiring investigation (FailedMessages)
    /// </remarks>
    IInboxProcessorMetrics GetMetrics();
}