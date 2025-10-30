using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

/// <summary>
/// Defines the contract for managing dead-letter queues in the HeroMessaging system.
/// Dead-letter queues store messages that cannot be processed successfully for later inspection, retry, or discard.
/// </summary>
/// <remarks>
/// Dead-letter queues are a critical component of resilient distributed systems, providing:
/// - Permanent storage for failed messages that exceeded retry limits
/// - Visibility into processing failures for debugging and monitoring
/// - Ability to retry messages after fixing root causes
/// - Statistics and analytics on failure patterns
///
/// Common use cases:
/// - Messages that fail after max retries (transient errors that won't resolve)
/// - Messages with invalid data requiring manual correction
/// - Messages affected by bugs in handler code (retry after deploying fix)
/// - Messages from deprecated versions that need migration
///
/// Typical workflow:
/// 1. Message processing fails repeatedly
/// 2. Error handler sends message to dead-letter queue
/// 3. Operators investigate and fix root cause (code bug, data issue, external service)
/// 4. Retry messages from dead-letter queue
/// 5. Discard messages that are truly invalid
///
/// Example usage:
/// <code>
/// // Send failed message to dead-letter queue
/// var deadLetterId = await deadLetterQueue.SendToDeadLetter(message, new DeadLetterContext
/// {
///     Reason = "Max retries exceeded",
///     Component = "OrderProcessingHandler",
///     RetryCount = 3,
///     Exception = lastException,
///     Metadata = new Dictionary&lt;string, object&gt;
///     {
///         ["ErrorCode"] = "TIMEOUT",
///         ["OriginalQueue"] = "orders"
///     }
/// });
///
/// // Later: Get dead-lettered messages for inspection
/// var deadLetters = await deadLetterQueue.GetDeadLetters&lt;OrderCreatedEvent&gt;(limit: 50);
/// foreach (var entry in deadLetters)
/// {
///     Console.WriteLine($"Failed: {entry.Context.Reason} at {entry.CreatedAt}");
/// }
///
/// // After fixing issue: Retry specific message
/// bool retried = await deadLetterQueue.Retry&lt;OrderCreatedEvent&gt;(deadLetterId);
///
/// // Or discard if message is invalid
/// bool discarded = await deadLetterQueue.Discard(deadLetterId);
///
/// // Monitor dead-letter queue health
/// var stats = await deadLetterQueue.GetStatistics();
/// Console.WriteLine($"Active: {stats.ActiveCount}, Retried: {stats.RetriedCount}");
/// </code>
/// </remarks>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Sends a failed message to the dead-letter queue for later inspection or retry.
    /// </summary>
    /// <typeparam name="T">The type of message being dead-lettered. Must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="message">The message that failed processing</param>
    /// <param name="context">Context information about the failure including reason, exception, and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing the unique identifier for the dead-letter entry</returns>
    /// <exception cref="ArgumentNullException">Thrown when message or context is null</exception>
    /// <remarks>
    /// This method permanently stores the failed message along with diagnostic information.
    /// Messages remain in the dead-letter queue until explicitly retried or discarded.
    ///
    /// The returned ID can be used to:
    /// - Retry the specific message with <see cref="Retry{T}"/>
    /// - Discard the message with <see cref="Discard"/>
    /// - Track the message in monitoring systems
    ///
    /// Best practices:
    /// - Include detailed reason explaining why message was dead-lettered
    /// - Capture the original exception for debugging
    /// - Add metadata for context (queue name, correlation ID, user ID)
    /// - Monitor dead-letter queue growth to detect systemic issues
    /// </remarks>
    Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Retrieves dead-lettered messages of a specific type for inspection.
    /// </summary>
    /// <typeparam name="T">The type of messages to retrieve. Must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="limit">Maximum number of entries to return. Default: 100. Use for pagination.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing the collection of dead-letter entries, ordered by creation time (newest first)</returns>
    /// <remarks>
    /// Use this method to:
    /// - Inspect failed messages for debugging
    /// - Build dead-letter queue monitoring dashboards
    /// - Identify patterns in failures (common reasons, affected components)
    /// - Export messages for offline analysis
    ///
    /// Only returns messages of type <typeparamref name="T"/> with status <see cref="DeadLetterStatus.Active"/>.
    /// Retried and discarded messages are excluded from results but remain in storage for audit.
    ///
    /// Example:
    /// <code>
    /// // Get first 50 failed order messages
    /// var deadLetters = await deadLetterQueue.GetDeadLetters&lt;OrderCreatedEvent&gt;(limit: 50);
    ///
    /// // Analyze failure reasons
    /// var reasonGroups = deadLetters
    ///     .GroupBy(e =&gt; e.Context.Reason)
    ///     .Select(g =&gt; new { Reason = g.Key, Count = g.Count() });
    ///
    /// // Find oldest failures
    /// var oldest = deadLetters.OrderBy(e =&gt; e.CreatedAt).First();
    /// Console.WriteLine($"Oldest failure: {oldest.CreatedAt}");
    /// </code>
    /// </remarks>
    Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Retries a dead-lettered message by re-submitting it for processing.
    /// </summary>
    /// <typeparam name="T">The type of message to retry. Must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="deadLetterId">The unique identifier of the dead-letter entry to retry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing true if the message was successfully queued for retry, false if the entry
    /// was not found or already processed (retried/discarded)
    /// </returns>
    /// <remarks>
    /// This method re-submits the message for processing through the normal message handling pipeline.
    /// The dead-letter entry status changes to <see cref="DeadLetterStatus.Retried"/> and
    /// <see cref="DeadLetterEntry{T}.RetriedAt"/> is set to the current time.
    ///
    /// Use after:
    /// - Fixing bugs in message handler code
    /// - Resolving external service outages
    /// - Correcting data issues
    /// - Deploying configuration changes
    ///
    /// The retried message will go through normal processing including:
    /// - Validation
    /// - Handler execution
    /// - Error handling (could be dead-lettered again if still failing)
    ///
    /// Example workflow:
    /// <code>
    /// // 1. Deploy fix for bug in OrderProcessingHandler
    /// // 2. Get all failed orders
    /// var failedOrders = await deadLetterQueue.GetDeadLetters&lt;OrderCreatedEvent&gt;();
    ///
    /// // 3. Retry messages affected by this bug
    /// foreach (var entry in failedOrders.Where(e =&gt; e.Context.Reason.Contains("NullReference")))
    /// {
    ///     bool success = await deadLetterQueue.Retry&lt;OrderCreatedEvent&gt;(entry.Id);
    ///     if (success)
    ///         Console.WriteLine($"Retrying order {entry.Message.MessageId}");
    /// }
    /// </code>
    /// </remarks>
    Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage;

    /// <summary>
    /// Permanently discards a dead-lettered message without processing it.
    /// </summary>
    /// <param name="deadLetterId">The unique identifier of the dead-letter entry to discard</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A task containing true if the message was successfully discarded, false if the entry
    /// was not found or already processed (retried/discarded)
    /// </returns>
    /// <remarks>
    /// This method marks the entry as discarded but retains it in storage for audit purposes.
    /// The entry status changes to <see cref="DeadLetterStatus.Discarded"/> and
    /// <see cref="DeadLetterEntry{T}.DiscardedAt"/> is set to the current time.
    ///
    /// Use for messages that:
    /// - Are truly invalid and cannot be corrected
    /// - Come from deprecated/obsolete features
    /// - Are duplicates of successfully processed messages
    /// - Contain sensitive data that should not be retried
    ///
    /// Discarded messages:
    /// - No longer appear in <see cref="GetDeadLetters{T}"/> results
    /// - Are excluded from <see cref="DeadLetterStatistics.ActiveCount"/>
    /// - Remain in storage for compliance/audit (until retention policies remove them)
    ///
    /// WARNING: Discarded messages cannot be retried. Ensure the message is truly invalid
    /// before discarding. Consider exporting the message first if it might be needed later.
    ///
    /// Example:
    /// <code>
    /// // Get messages with permanent validation errors
    /// var invalidMessages = await deadLetterQueue.GetDeadLetters&lt;OrderCreatedEvent&gt;();
    ///
    /// foreach (var entry in invalidMessages)
    /// {
    ///     if (entry.Context.Reason.Contains("Invalid customer ID"))
    ///     {
    ///         // Log for audit before discarding
    ///         logger.LogWarning($"Discarding invalid order: {entry.Message.MessageId}");
    ///
    ///         // Permanently discard
    ///         await deadLetterQueue.Discard(entry.Id);
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of active (not retried/discarded) dead-lettered messages across all types.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing the count of active dead-letter entries</returns>
    /// <remarks>
    /// Use for:
    /// - Monitoring dead-letter queue health
    /// - Setting up alerts when count exceeds threshold
    /// - Capacity planning and storage management
    /// - Health check endpoints
    ///
    /// This count includes messages of all types with status <see cref="DeadLetterStatus.Active"/>.
    /// Retried and discarded messages are excluded.
    ///
    /// Example:
    /// <code>
    /// // Health check
    /// var count = await deadLetterQueue.GetDeadLetterCount();
    /// if (count > 1000)
    /// {
    ///     logger.LogWarning($"High dead-letter count: {count}");
    ///     // Alert operations team
    /// }
    /// </code>
    /// </remarks>
    Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive statistics about dead-lettered messages including counts by status, component, and reason.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing detailed <see cref="DeadLetterStatistics"/> for analysis and monitoring</returns>
    /// <remarks>
    /// Provides deep insights into failure patterns:
    /// - Status breakdown: Active, Retried, Discarded counts
    /// - Component analysis: Which handlers are failing most
    /// - Reason analysis: Common failure causes
    /// - Time range: Oldest and newest entries
    ///
    /// Use for:
    /// - Building monitoring dashboards
    /// - Identifying systemic issues (high failure rate in specific component)
    /// - Capacity planning (growth trends)
    /// - Root cause analysis (common failure reasons)
    /// - SLA reporting (time messages spend in dead-letter queue)
    ///
    /// Example:
    /// <code>
    /// var stats = await deadLetterQueue.GetStatistics();
    ///
    /// // Overall health
    /// Console.WriteLine($"Active: {stats.ActiveCount}, Total: {stats.TotalCount}");
    ///
    /// // Component failure analysis
    /// var topFailingComponent = stats.CountByComponent
    ///     .OrderByDescending(kvp =&gt; kvp.Value)
    ///     .First();
    /// Console.WriteLine($"Most failures in: {topFailingComponent.Key} ({topFailingComponent.Value})");
    ///
    /// // Common failure reasons
    /// foreach (var reason in stats.CountByReason.OrderByDescending(kvp =&gt; kvp.Value).Take(5))
    /// {
    ///     Console.WriteLine($"{reason.Key}: {reason.Value}");
    /// }
    ///
    /// // Age of oldest failure
    /// if (stats.OldestEntry.HasValue)
    /// {
    ///     var age = DateTime.UtcNow - stats.OldestEntry.Value;
    ///     Console.WriteLine($"Oldest entry is {age.TotalHours:F1} hours old");
    /// }
    /// </code>
    /// </remarks>
    Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides context information about a message being sent to the dead-letter queue.
/// Contains failure details, diagnostic information, and metadata for troubleshooting.
/// </summary>
/// <remarks>
/// This class captures comprehensive information about why a message failed,
/// enabling effective debugging and root cause analysis.
///
/// Best practices:
/// - Provide clear, actionable reasons
/// - Include the original exception for stack traces
/// - Add component name for tracking failure sources
/// - Record retry count to understand persistence of failures
/// - Use metadata for additional context (queue, correlation ID, user)
///
/// Example:
/// <code>
/// var context = new DeadLetterContext
/// {
///     Reason = "Payment service timeout after 3 retries",
///     Exception = timeoutException,
///     Component = "PaymentProcessingHandler",
///     RetryCount = 3,
///     FailureTime = DateTime.UtcNow,
///     Metadata = new Dictionary&lt;string, object&gt;
///     {
///         ["OriginalQueue"] = "payments",
///         ["CorrelationId"] = correlationId,
///         ["PaymentGateway"] = "Stripe",
///         ["ErrorCode"] = "GATEWAY_TIMEOUT"
///     }
/// };
/// </code>
/// </remarks>
public class DeadLetterContext
{
    /// <summary>
    /// Gets or sets the human-readable reason why the message was sent to dead-letter queue.
    /// Should be descriptive enough for operators to understand the issue.
    /// </summary>
    /// <remarks>
    /// Good reasons include:
    /// - "Max retries exceeded: timeout connecting to payment service"
    /// - "Invalid customer ID: customer not found in database"
    /// - "Message schema version mismatch: expected v2, got v1"
    /// - "Handler threw unhandled exception: NullReferenceException in OrderValidator"
    ///
    /// Avoid vague reasons like "Error" or "Failed" - be specific about what went wrong.
    /// </remarks>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exception that caused the message to be dead-lettered, if applicable.
    /// Provides stack trace and detailed error information for debugging.
    /// </summary>
    /// <remarks>
    /// This should be the last exception that occurred before dead-lettering.
    /// If message failed multiple times, include the most recent exception.
    ///
    /// Null if message was dead-lettered for non-exception reasons
    /// (e.g., business rule violations, manual intervention).
    /// </remarks>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the name of the component or handler where the failure originated.
    /// Used to identify which part of the system is experiencing issues.
    /// </summary>
    /// <remarks>
    /// Examples: "OrderProcessingHandler", "PaymentService", "InventoryUpdater"
    ///
    /// Use statistics by component to identify systemic issues:
    /// - High failure rate in specific handler may indicate bugs
    /// - Failures across multiple components may indicate infrastructure issues
    /// </remarks>
    public string Component { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of times the message was retried before being dead-lettered.
    /// Indicates persistence of the failure.
    /// </summary>
    /// <remarks>
    /// High retry counts indicate:
    /// - Persistent transient errors (ongoing service outage)
    /// - Systematic issues (bug affecting all similar messages)
    /// - Configuration problems (wrong credentials, endpoints)
    ///
    /// Compare against max retry policies to understand retry behavior.
    /// </remarks>
    public int RetryCount { get; set; }

    /// <summary>
    /// Gets or sets when the failure occurred (when message was sent to dead-letter queue).
    /// Defaults to current UTC time.
    /// </summary>
    /// <remarks>
    /// Different from <see cref="DeadLetterEntry{T}.CreatedAt"/> which is set by storage implementation.
    /// This represents the logical failure time, while CreatedAt is the physical storage time.
    ///
    /// Use to:
    /// - Track how long messages have been failing
    /// - Correlate failures with deployments or incidents
    /// - Calculate time to resolution
    /// </remarks>
    public DateTime FailureTime { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

    /// <summary>
    /// Gets or sets custom metadata associated with the failure.
    /// Can contain any diagnostic information relevant to troubleshooting.
    /// </summary>
    /// <remarks>
    /// Common metadata:
    /// - "OriginalQueue": Name of queue where message was being processed
    /// - "CorrelationId": Distributed tracing identifier
    /// - "ErrorCode": Application-specific error code
    /// - "Endpoint": External service URL that failed
    /// - "UserId": User context when failure occurred
    /// - "TenantId": Multi-tenant identifier
    /// - "MessageVersion": Schema version of the message
    /// - "DeploymentVersion": Application version when failure occurred
    /// </remarks>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Represents a message stored in the dead-letter queue along with its context and status.
/// </summary>
/// <typeparam name="T">The type of message that was dead-lettered. Must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// Contains the complete information about a dead-lettered message:
/// - Original message data
/// - Failure context (reason, exception, metadata)
/// - Lifecycle status (active, retried, discarded)
/// - Timestamps for tracking
///
/// Retrieved by <see cref="IDeadLetterQueue.GetDeadLetters{T}"/> for inspection and management.
/// </remarks>
public class DeadLetterEntry<T> where T : IMessage
{
    /// <summary>
    /// Gets or sets the unique identifier for this dead-letter entry.
    /// Used to retry or discard the message.
    /// </summary>
    /// <remarks>
    /// Automatically generated as a new GUID when the entry is created.
    /// Pass this ID to <see cref="IDeadLetterQueue.Retry{T}"/> or <see cref="IDeadLetterQueue.Discard"/>
    /// to process the entry.
    /// </remarks>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the original message that failed processing.
    /// </summary>
    /// <remarks>
    /// This is the complete message object as it was when processing failed.
    /// Inspect this to understand what data caused the failure.
    ///
    /// When retrying, this exact message is re-submitted for processing.
    /// </remarks>
    public T Message { get; set; } = default!;

    /// <summary>
    /// Gets or sets the context information about why and how the message failed.
    /// See <see cref="DeadLetterContext"/> for details.
    /// </summary>
    public DeadLetterContext Context { get; set; } = new();

    /// <summary>
    /// Gets or sets when this entry was created in the dead-letter queue.
    /// Defaults to current UTC time.
    /// </summary>
    /// <remarks>
    /// Use to:
    /// - Calculate how long message has been in dead-letter queue
    /// - Sort entries by age
    /// - Implement retention policies (delete entries older than X days)
    /// - Measure time to resolution (CreatedAt to RetriedAt/DiscardedAt)
    /// </remarks>
    public DateTime CreatedAt { get; set; } = TimeProvider.System.GetUtcNow().DateTime;

    /// <summary>
    /// Gets or sets the current status of this dead-letter entry.
    /// See <see cref="DeadLetterStatus"/> for possible values.
    /// Defaults to <see cref="DeadLetterStatus.Active"/>.
    /// </summary>
    /// <remarks>
    /// Status transitions:
    /// - Active: Initial state, awaiting retry or discard decision
    /// - Retried: Message was re-submitted for processing
    /// - Discarded: Message was permanently discarded
    /// - Expired: Message exceeded retention period (if implemented)
    ///
    /// Only Active entries are returned by <see cref="IDeadLetterQueue.GetDeadLetters{T}"/>.
    /// </remarks>
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Active;

    /// <summary>
    /// Gets or sets when this entry was retried, if applicable.
    /// Null if entry has not been retried.
    /// </summary>
    /// <remarks>
    /// Set when <see cref="IDeadLetterQueue.Retry{T}"/> is called successfully.
    /// Use to track:
    /// - How long message was in dead-letter queue before retry
    /// - When fixes were applied (correlate with deployments)
    /// - Success rate of retries
    /// </remarks>
    public DateTime? RetriedAt { get; set; }

    /// <summary>
    /// Gets or sets when this entry was discarded, if applicable.
    /// Null if entry has not been discarded.
    /// </summary>
    /// <remarks>
    /// Set when <see cref="IDeadLetterQueue.Discard"/> is called successfully.
    /// Use to:
    /// - Track cleanup operations
    /// - Audit message disposal
    /// - Calculate discard rate
    /// </remarks>
    public DateTime? DiscardedAt { get; set; }
}

/// <summary>
/// Defines the lifecycle status of a dead-letter entry.
/// </summary>
/// <remarks>
/// Used in <see cref="DeadLetterEntry{T}.Status"/> to track what happened to a dead-lettered message.
/// </remarks>
public enum DeadLetterStatus
{
    /// <summary>
    /// Entry is active and awaiting action (retry or discard).
    /// This is the initial state when a message is first dead-lettered.
    /// Active entries appear in <see cref="IDeadLetterQueue.GetDeadLetters{T}"/> results.
    /// </summary>
    Active,

    /// <summary>
    /// Entry has been retried (re-submitted for processing).
    /// Set when <see cref="IDeadLetterQueue.Retry{T}"/> is called.
    /// Retried entries are excluded from active counts and queries.
    /// </summary>
    Retried,

    /// <summary>
    /// Entry has been permanently discarded.
    /// Set when <see cref="IDeadLetterQueue.Discard"/> is called.
    /// Discarded entries are excluded from active counts and queries.
    /// </summary>
    Discarded,

    /// <summary>
    /// Entry has expired due to retention policies.
    /// Automatically set when entry exceeds configured retention period.
    /// Expired entries may be archived or deleted based on storage implementation.
    /// </summary>
    Expired
}

/// <summary>
/// Provides comprehensive statistics about the dead-letter queue for monitoring and analysis.
/// </summary>
/// <remarks>
/// Returned by <see cref="IDeadLetterQueue.GetStatistics"/> to provide insights into:
/// - Overall queue health (total and active counts)
/// - Failure patterns (by component and reason)
/// - Time ranges (oldest and newest entries)
///
/// Use for:
/// - Monitoring dashboards
/// - Alerting on high failure rates
/// - Root cause analysis
/// - Capacity planning
/// - SLA tracking
///
/// Example dashboard queries:
/// <code>
/// var stats = await deadLetterQueue.GetStatistics();
///
/// // Health metrics
/// var activeRate = (double)stats.ActiveCount / stats.TotalCount * 100;
/// var retrySuccessRate = (double)stats.RetriedCount / stats.TotalCount * 100;
///
/// // Top failing components
/// var topComponents = stats.CountByComponent
///     .OrderByDescending(kvp =&gt; kvp.Value)
///     .Take(5);
///
/// // Common failure reasons
/// var topReasons = stats.CountByReason
///     .OrderByDescending(kvp =&gt; kvp.Value)
///     .Take(10);
///
/// // Age tracking
/// if (stats.OldestEntry.HasValue)
/// {
///     var maxAge = DateTime.UtcNow - stats.OldestEntry.Value;
///     if (maxAge.TotalDays > 7)
///         logger.LogWarning($"Messages stuck in DLQ for {maxAge.TotalDays:F1} days");
/// }
/// </code>
/// </remarks>
public class DeadLetterStatistics
{
    /// <summary>
    /// Gets or sets the total count of all dead-letter entries regardless of status.
    /// Includes active, retried, discarded, and expired entries.
    /// </summary>
    /// <remarks>
    /// Use to track overall failure volume and trends over time.
    /// Compare with <see cref="ActiveCount"/> to understand cleanup effectiveness.
    /// </remarks>
    public long TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the count of active dead-letter entries awaiting action.
    /// Only includes entries with status <see cref="DeadLetterStatus.Active"/>.
    /// </summary>
    /// <remarks>
    /// This is the most important metric for monitoring.
    /// High or growing active count indicates:
    /// - Ongoing failures requiring attention
    /// - Insufficient retry/cleanup operations
    /// - Systemic issues in message processing
    ///
    /// Set up alerts when this exceeds thresholds (e.g., > 1000).
    /// </remarks>
    public long ActiveCount { get; set; }

    /// <summary>
    /// Gets or sets the count of entries that were successfully retried.
    /// Only includes entries with status <see cref="DeadLetterStatus.Retried"/>.
    /// </summary>
    /// <remarks>
    /// Use to measure:
    /// - Effectiveness of retry operations
    /// - Recovery from transient failures
    /// - Impact of bug fixes (spike in retries after deployment)
    ///
    /// Compare with <see cref="TotalCount"/> to calculate retry success rate.
    /// </remarks>
    public long RetriedCount { get; set; }

    /// <summary>
    /// Gets or sets the count of entries that were permanently discarded.
    /// Only includes entries with status <see cref="DeadLetterStatus.Discarded"/>.
    /// </summary>
    /// <remarks>
    /// High discard count may indicate:
    /// - Many invalid messages reaching the system
    /// - Overly strict validation rules
    /// - Data quality issues upstream
    ///
    /// Investigate if discard rate is abnormally high.
    /// </remarks>
    public long DiscardedCount { get; set; }

    /// <summary>
    /// Gets or sets the count of entries grouped by component name.
    /// Key: component name, Value: number of failures in that component.
    /// </summary>
    /// <remarks>
    /// Use to identify which handlers/components are failing most frequently.
    /// Helpful for:
    /// - Prioritizing bug fixes (focus on highest-failure components)
    /// - Identifying systematic issues
    /// - Assigning ownership for resolution
    ///
    /// Example:
    /// <code>
    /// // Find component with most failures
    /// var topComponent = stats.CountByComponent
    ///     .OrderByDescending(kvp =&gt; kvp.Value)
    ///     .FirstOrDefault();
    /// if (topComponent.Value > 100)
    ///     logger.LogWarning($"High failure rate in {topComponent.Key}: {topComponent.Value}");
    /// </code>
    /// </remarks>
    public Dictionary<string, long> CountByComponent { get; set; } = new();

    /// <summary>
    /// Gets or sets the count of entries grouped by failure reason.
    /// Key: failure reason text, Value: number of entries with that reason.
    /// </summary>
    /// <remarks>
    /// Use to identify common failure patterns:
    /// - "Timeout": Network or performance issues
    /// - "Not found": Data consistency problems
    /// - "Validation failed": Data quality issues
    /// - Specific error codes: Known bugs or limitations
    ///
    /// Example:
    /// <code>
    /// // Analyze failure reasons
    /// var timeouts = stats.CountByReason
    ///     .Where(kvp =&gt; kvp.Key.Contains("timeout", StringComparison.OrdinalIgnoreCase))
    ///     .Sum(kvp =&gt; kvp.Value);
    /// if (timeouts > 0)
    ///     logger.LogWarning($"{timeouts} timeout-related failures detected");
    /// </code>
    /// </remarks>
    public Dictionary<string, long> CountByReason { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp of the oldest active entry in the dead-letter queue.
    /// Null if there are no entries.
    /// </summary>
    /// <remarks>
    /// Use to track how long messages have been stuck:
    /// - Messages stuck for days/weeks indicate unresolved issues
    /// - Growing age suggests insufficient cleanup/retry operations
    /// - Set up alerts for entries older than SLA thresholds
    ///
    /// Example:
    /// <code>
    /// if (stats.OldestEntry.HasValue)
    /// {
    ///     var age = DateTime.UtcNow - stats.OldestEntry.Value;
    ///     if (age.TotalHours > 24)
    ///         logger.LogError($"Messages in DLQ for {age.TotalHours:F1} hours");
    /// }
    /// </code>
    /// </remarks>
    public DateTime? OldestEntry { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the newest entry in the dead-letter queue.
    /// Null if there are no entries.
    /// </summary>
    /// <remarks>
    /// Use to:
    /// - Detect recent failures (if NewestEntry is very recent, failures are ongoing)
    /// - Identify when failure patterns started
    /// - Correlate with deployments or incidents
    ///
    /// Compare with <see cref="OldestEntry"/> to understand failure time span.
    /// </remarks>
    public DateTime? NewestEntry { get; set; }
}