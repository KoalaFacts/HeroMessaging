using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides persistent queue storage for background message processing and job scheduling.
/// </summary>
/// <remarks>
/// Queue storage enables asynchronous message processing patterns including:
/// - Background job processing
/// - Delayed message delivery
/// - Priority-based processing
/// - Rate limiting and throttling
/// - Work distribution across multiple consumers
///
/// Queues support visibility timeout semantics (similar to SQS/RabbitMQ) to prevent
/// message loss if a consumer crashes during processing. Messages become invisible
/// when dequeued and must be explicitly acknowledged or rejected.
///
/// Example usage:
/// <code>
/// // Producer: Add messages to queue
/// await queueStorage.CreateQueue("email-notifications", new QueueOptions
/// {
///     MaxSize = 10000,
///     MessageTtl = TimeSpan.FromDays(7),
///     VisibilityTimeout = TimeSpan.FromMinutes(5)
/// });
///
/// await queueStorage.Enqueue("email-notifications", new SendEmailCommand
/// {
///     To = "user@example.com",
///     Subject = "Welcome!"
/// }, new EnqueueOptions
/// {
///     Priority = 1,
///     Delay = TimeSpan.FromMinutes(5)
/// });
///
/// // Consumer: Process messages from queue
/// while (running)
/// {
///     var entry = await queueStorage.Dequeue("email-notifications");
///     if (entry == null)
///     {
///         await Task.Delay(1000);
///         continue;
///     }
///
///     try
///     {
///         await ProcessMessageAsync(entry.Message);
///         await queueStorage.Acknowledge("email-notifications", entry.Id);
///     }
///     catch (Exception ex)
///     {
///         await queueStorage.Reject("email-notifications", entry.Id, requeue: true);
///     }
/// }
/// </code>
/// </remarks>
public interface IQueueStorage
{
    /// <summary>
    /// Adds a message to the specified queue for background processing.
    /// </summary>
    /// <param name="queueName">The name of the queue to add the message to</param>
    /// <param name="message">The message to enqueue</param>
    /// <param name="options">Optional enqueue configuration including priority, delay, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created queue entry with assigned ID and metadata</returns>
    /// <remarks>
    /// Messages are added to the queue and become available for dequeue based on:
    /// - Priority (higher priority messages are dequeued first)
    /// - Delay (messages with delay become visible after the specified duration)
    /// - FIFO order (within same priority level)
    ///
    /// If the queue doesn't exist, implementations may auto-create it or throw an exception.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when queue is full (if MaxSize is configured)</exception>
    Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and removes the next available message from the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to dequeue from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The next queue entry if available; otherwise null</returns>
    /// <remarks>
    /// Dequeue operations are atomic and use visibility timeout semantics:
    /// 1. Message is removed from queue and becomes invisible to other consumers
    /// 2. VisibleAt timestamp is set (now + VisibilityTimeout)
    /// 3. Consumer processes the message
    /// 4. Consumer must Acknowledge (success) or Reject (failure)
    /// 5. If neither occurs before VisibleAt, message automatically returns to queue
    ///
    /// Messages are returned in priority order (highest first), then FIFO within priority.
    /// Messages with future VisibleAt timestamps are skipped.
    ///
    /// Returns null if no messages are available (queue is empty or all messages are invisible).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when queue doesn't exist</exception>
    Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves messages from the queue without removing them (preview mode).
    /// </summary>
    /// <param name="queueName">The name of the queue to peek into</param>
    /// <param name="count">Maximum number of messages to retrieve. Default is 1</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue entries up to the specified count</returns>
    /// <remarks>
    /// Peek allows inspecting queue contents without affecting message visibility or position.
    /// Use this for:
    /// - Monitoring queue contents
    /// - Debugging message processing issues
    /// - Building queue management dashboards
    ///
    /// Messages are returned in the same order they would be dequeued (priority, then FIFO).
    /// Peeked messages remain in the queue and visible to other consumers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges successful processing of a dequeued message, permanently removing it from the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the entry</param>
    /// <param name="entryId">The unique identifier of the queue entry to acknowledge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was acknowledged and removed; false if not found</returns>
    /// <remarks>
    /// Always acknowledge messages after successful processing to prevent them from
    /// returning to the queue when the visibility timeout expires.
    ///
    /// If the entry is not found (already acknowledged or visibility timeout expired),
    /// returns false. The message may have been requeued and processed by another consumer.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a dequeued message, optionally returning it to the queue for retry.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the entry</param>
    /// <param name="entryId">The unique identifier of the queue entry to reject</param>
    /// <param name="requeue">If true, message is returned to queue for retry. If false, message is permanently removed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was rejected; false if not found</returns>
    /// <remarks>
    /// Use reject to handle processing failures:
    /// - requeue = true: Transient errors (network, temporary unavailability) - message can be retried
    /// - requeue = false: Permanent errors (invalid data, poison messages) - message should be removed
    ///
    /// When requeuing, DequeueCount is incremented. If DequeueCount exceeds MaxDequeueCount
    /// (from QueueOptions), the message is moved to a dead-letter queue or permanently removed.
    ///
    /// If the entry is not found, returns false (may have already been acknowledged or timed out).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null</exception>
    Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of messages in the queue (pending + invisible).
    /// </summary>
    /// <param name="queueName">The name of the queue to measure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The total number of messages in the queue, including invisible messages</returns>
    /// <remarks>
    /// Use this for monitoring and capacity planning. Queue depth indicates:
    /// - Processing backlog size
    /// - System load and capacity requirements
    /// - Need for scaling consumers
    ///
    /// Depth includes both visible (ready for dequeue) and invisible (currently being processed)
    /// messages. Does not include messages that have been acknowledged.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new queue with the specified options.
    /// </summary>
    /// <param name="queueName">The name of the queue to create</param>
    /// <param name="options">Optional queue configuration including size limits, TTL, and visibility timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was created; false if it already exists</returns>
    /// <remarks>
    /// Queue configuration options include:
    /// - MaxSize: Maximum number of messages (prevents unbounded growth)
    /// - MessageTtl: Automatic message expiration
    /// - MaxDequeueCount: Maximum retry attempts before dead-lettering
    /// - VisibilityTimeout: How long messages remain invisible after dequeue
    /// - EnablePriority: Whether to support priority-based ordering
    ///
    /// Example:
    /// <code>
    /// await queueStorage.CreateQueue("critical-jobs", new QueueOptions
    /// {
    ///     MaxSize = 1000,
    ///     MessageTtl = TimeSpan.FromHours(24),
    ///     MaxDequeueCount = 5,
    ///     VisibilityTimeout = TimeSpan.FromMinutes(10),
    ///     EnablePriority = true
    /// });
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a queue and all its messages.
    /// </summary>
    /// <param name="queueName">The name of the queue to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was deleted; false if it doesn't exist</returns>
    /// <remarks>
    /// WARNING: This operation permanently deletes the queue and all contained messages.
    /// Messages in-flight (dequeued but not acknowledged) are also lost.
    ///
    /// Ensure all consumers have stopped processing before deleting a queue.
    /// Consider draining the queue (process all messages) before deletion if needed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the names of all existing queues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue names</returns>
    /// <remarks>
    /// Use this for:
    /// - Queue discovery and management
    /// - Monitoring dashboards
    /// - Administrative tools
    /// - Dynamic queue creation/deletion logic
    /// </remarks>
    Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a queue with the specified name exists.
    /// </summary>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue exists; otherwise false</returns>
    /// <remarks>
    /// Use this before enqueue/dequeue operations if you need to handle
    /// missing queues gracefully (e.g., auto-create or log warning).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a message stored in a queue awaiting processing.
/// </summary>
/// <remarks>
/// Queue entries track the lifecycle of messages in the queue, including
/// visibility state, dequeue attempts, and scheduling information.
/// </remarks>
public class QueueEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this queue entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the message to be processed.
    /// </summary>
    public IMessage Message { get; set; } = null!;

    /// <summary>
    /// Gets or sets the enqueue options including priority, delay, and metadata.
    /// See <see cref="EnqueueOptions"/> for available configuration options.
    /// </summary>
    public EnqueueOptions Options { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this message was added to the queue.
    /// </summary>
    public DateTime EnqueuedAt { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

    /// <summary>
    /// Gets or sets the timestamp when this message becomes visible for dequeue.
    /// </summary>
    /// <remarks>
    /// This timestamp controls message visibility:
    /// - Initially: EnqueuedAt + Delay (from EnqueueOptions)
    /// - After dequeue: Current time + VisibilityTimeout (from QueueOptions)
    /// - After reject with requeue: Current time (immediately available)
    ///
    /// Messages with future VisibleAt timestamps are invisible to Dequeue operations.
    /// If a dequeued message is not acknowledged before VisibleAt expires, it automatically
    /// returns to the queue (becomes visible again).
    /// </remarks>
    public DateTime? VisibleAt { get; set; }

    /// <summary>
    /// Gets or sets the number of times this message has been dequeued.
    /// </summary>
    /// <remarks>
    /// Incremented each time the message is dequeued. Used for:
    /// - Poison message detection (messages that repeatedly fail)
    /// - Dead-letter queue routing (when exceeds MaxDequeueCount)
    /// - Monitoring and alerting on problematic messages
    ///
    /// If DequeueCount exceeds MaxDequeueCount (from QueueOptions), the message
    /// is typically moved to a dead-letter queue or permanently removed.
    /// </remarks>
    public int DequeueCount { get; set; }
}

/// <summary>
/// Configuration options for creating and managing queues.
/// </summary>
/// <remarks>
/// These options control queue behavior including capacity limits, message retention,
/// retry policies, and visibility timeout semantics.
/// </remarks>
public class QueueOptions
{
    /// <summary>
    /// Gets or sets the maximum number of messages the queue can hold.
    /// </summary>
    /// <remarks>
    /// If set, Enqueue operations will fail when the queue reaches this size.
    /// This prevents unbounded memory growth and provides backpressure to producers.
    ///
    /// If null, the queue has no size limit (not recommended for production).
    /// Typical values: 1,000 - 100,000 depending on message size and processing rate.
    /// </remarks>
    public int? MaxSize { get; set; }

    /// <summary>
    /// Gets or sets the time-to-live for messages in the queue.
    /// Messages older than this duration are automatically removed.
    /// </summary>
    /// <remarks>
    /// Use this to implement automatic cleanup of stale messages.
    /// Useful for time-sensitive operations where old messages are no longer relevant.
    ///
    /// If null, messages never expire automatically.
    /// Typical values: 1 hour - 7 days depending on business requirements.
    /// </remarks>
    public TimeSpan? MessageTtl { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of times a message can be dequeued before being dead-lettered.
    /// </summary>
    /// <remarks>
    /// Prevents poison messages (messages that always fail) from blocking queue processing.
    /// After MaxDequeueCount attempts, messages are moved to a dead-letter queue or removed.
    ///
    /// If null, messages can be retried indefinitely (not recommended).
    /// Typical values: 3-10 depending on error tolerance and retry strategy.
    /// </remarks>
    public int? MaxDequeueCount { get; set; }

    /// <summary>
    /// Gets or sets how long dequeued messages remain invisible before returning to the queue.
    /// </summary>
    /// <remarks>
    /// This implements visibility timeout semantics (similar to AWS SQS):
    /// - When a message is dequeued, it becomes invisible to other consumers
    /// - The consumer has VisibilityTimeout duration to process and acknowledge
    /// - If not acknowledged within this time, message automatically returns to queue
    ///
    /// Set this based on expected processing time plus a safety margin.
    /// Too short: Messages may be reprocessed while still being handled
    /// Too long: Failed consumers cause delayed retries
    ///
    /// If null, uses system default (typically 30 seconds).
    /// Typical values: 30 seconds - 10 minutes.
    /// </remarks>
    public TimeSpan? VisibilityTimeout { get; set; }

    /// <summary>
    /// Gets or sets whether to enable priority-based message ordering.
    /// Default is true.
    /// </summary>
    /// <remarks>
    /// When enabled:
    /// - Messages are dequeued in priority order (highest priority first)
    /// - Within same priority, FIFO order is maintained
    /// - Priority is specified in EnqueueOptions
    ///
    /// When disabled:
    /// - Strict FIFO ordering (may have better performance)
    /// - Priority field in EnqueueOptions is ignored
    ///
    /// Disable if you don't need priority and want simpler/faster queue operations.
    /// </remarks>
    public bool EnablePriority { get; set; } = true;
}