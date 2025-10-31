using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using System.Collections.Concurrent;

namespace HeroMessaging.Storage;

/// <summary>
/// Provides an in-memory implementation of queue storage for development, testing, and high-performance scenarios.
/// </summary>
/// <remarks>
/// This implementation stores queue entries in memory using concurrent dictionaries, making it suitable for:
/// - Development and testing environments
/// - High-performance in-process queuing without external dependencies
/// - Scenarios where queue persistence is not required
///
/// <para><strong>Thread Safety:</strong> This implementation is thread-safe and supports concurrent access
/// from multiple threads using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><strong>Volatility Warning:</strong> All queue entries are stored in memory and will be lost when
/// the application restarts or crashes. Messages in queues will be lost. Do not use this implementation
/// in production for scenarios requiring guaranteed message delivery or durability.</para>
///
/// <para><strong>Concurrency:</strong> All operations are lock-free and support high concurrency. Multiple
/// consumers can safely dequeue messages concurrently. However, the same message may be dequeued by
/// multiple consumers if dequeue operations overlap.</para>
///
/// <para><strong>Visibility Timeout:</strong> Supports visibility timeout for dequeued messages. Dequeued
/// messages become invisible for a configured duration and are automatically made visible again if not
/// acknowledged, enabling automatic retry for failed processing.</para>
///
/// <para><strong>Priority Ordering:</strong> Messages are dequeued based on priority (higher priority first)
/// and then by enqueue time (FIFO within same priority). Priority 0 is highest priority.</para>
///
/// <para><strong>Delayed Messages:</strong> Supports delayed message delivery via the Delay option in
/// EnqueueOptions. Delayed messages are not visible until the delay period has elapsed.</para>
/// </remarks>
public class InMemoryQueueStorage : IQueueStorage
{
    private readonly ConcurrentDictionary<string, Queue> _queues = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryQueueStorage"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for managing timestamps, delays, and visibility timeouts</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null</exception>
    public InMemoryQueueStorage(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Adds a message to the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to add the message to</param>
    /// <param name="message">The message to enqueue</param>
    /// <param name="options">Optional enqueue configuration including priority and delay</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created queue entry with assigned ID and metadata</returns>
    /// <remarks>
    /// The message is added with a GUID-based identifier. If a delay is specified in options,
    /// the message will not be visible for dequeue until the delay period has elapsed.
    ///
    /// If the queue does not exist, it is created automatically with default options.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or message is null</exception>
    public Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new Queue());
        var now = _timeProvider.GetUtcNow().DateTime;

        var entry = new QueueEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Options = options ?? new EnqueueOptions(),
            EnqueuedAt = now,
            VisibleAt = options?.Delay.HasValue == true
                ? now.Add(options.Delay.Value)
                : now
        };

        queue.Entries[entry.Id] = entry;
        return Task.FromResult(entry);
    }

    /// <summary>
    /// Retrieves and makes invisible the next available message from the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to dequeue from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The next queue entry if available; otherwise null</returns>
    /// <remarks>
    /// This method retrieves messages in priority order (higher priority first, where lower priority
    /// number = higher priority) and then by enqueue time (oldest first within same priority).
    ///
    /// The dequeued message becomes invisible for the configured visibility timeout period
    /// (default 1 minute). During this time, other consumers cannot dequeue the same message.
    ///
    /// If the message is not acknowledged within the visibility timeout, it automatically becomes
    /// visible again for retry. The dequeue count is incremented each time a message is dequeued.
    ///
    /// Messages are excluded from dequeue if:
    /// - They are not yet visible (VisibleAt is in the future due to delay or visibility timeout)
    /// - They have exceeded the maximum dequeue count (default 10)
    /// </remarks>
    public Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult<QueueEntry?>(null);
        }

        var now = _timeProvider.GetUtcNow().DateTime;
        var entry = queue.Entries.Values
            .Where(e => e.VisibleAt <= now && e.DequeueCount < (queue.Options?.MaxDequeueCount ?? 10))
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.EnqueuedAt)
            .FirstOrDefault();

        if (entry != null)
        {
            entry.DequeueCount++;
            entry.VisibleAt = now.Add(queue.Options?.VisibilityTimeout ?? TimeSpan.FromMinutes(1));
        }

        return Task.FromResult(entry);
    }

    /// <summary>
    /// Retrieves the next available messages from the queue without making them invisible.
    /// </summary>
    /// <param name="queueName">The name of the queue to peek into</param>
    /// <param name="count">Maximum number of messages to retrieve. Default is 1</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue entries in priority order, up to the specified count</returns>
    /// <remarks>
    /// This method allows inspecting messages without affecting their visibility or dequeue count.
    /// Messages are returned in the same order they would be dequeued (priority then FIFO).
    ///
    /// Only currently visible messages are included (VisibleAt in the past or now).
    ///
    /// Use this for monitoring queue contents or implementing custom processing logic without
    /// removing messages from the queue.
    /// </remarks>
    public Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(Enumerable.Empty<QueueEntry>());
        }

        var now = _timeProvider.GetUtcNow().DateTime;
        var entries = queue.Entries.Values
            .Where(e => e.VisibleAt <= now)
            .OrderByDescending(e => e.Options.Priority)
            .ThenBy(e => e.EnqueuedAt)
            .Take(count);

        return Task.FromResult(entries);
    }

    /// <summary>
    /// Acknowledges successful processing of a message and removes it from the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the message</param>
    /// <param name="entryId">The unique identifier of the queue entry to acknowledge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was acknowledged and removed; false if not found</returns>
    /// <remarks>
    /// Call this method after successfully processing a dequeued message to permanently remove it
    /// from the queue. If not acknowledged within the visibility timeout, the message will become
    /// visible again for retry.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null or empty</exception>
    public Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(queue.Entries.TryRemove(entryId, out _));
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Rejects a message and optionally requeues it for retry or removes it from the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue containing the message</param>
    /// <param name="entryId">The unique identifier of the queue entry to reject</param>
    /// <param name="requeue">If true, makes the message immediately visible for retry; if false, removes it from the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was rejected; false if not found</returns>
    /// <remarks>
    /// Use this method when message processing fails:
    /// - Set requeue = true to immediately make the message visible again for retry (resets dequeue count)
    /// - Set requeue = false to permanently remove the failed message from the queue
    ///
    /// When requeuing, the message becomes immediately visible (VisibleAt set to now) and the
    /// dequeue count is reset to 0, allowing it to be retried up to MaxDequeueCount times.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName or entryId is null or empty</exception>
    public Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            if (queue.Entries.TryGetValue(entryId, out var entry))
            {
                if (requeue)
                {
                    entry.VisibleAt = _timeProvider.GetUtcNow().DateTime;
                    entry.DequeueCount = 0;
                    return Task.FromResult(true);
                }
                else
                {
                    return Task.FromResult(queue.Entries.TryRemove(entryId, out _));
                }
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the current depth (number of visible messages) in the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of messages currently visible and available for dequeue</returns>
    /// <remarks>
    /// This count includes only messages that are currently visible (VisibleAt &lt;= now).
    /// Messages that are delayed or currently invisible due to dequeue visibility timeout are not included.
    ///
    /// Use this for monitoring and observability. A growing queue depth may indicate:
    /// - Consumer processing slower than producer publishing rate
    /// - Consumer failures or insufficient consumer capacity
    /// - Messages with long visibility timeouts or repeated failures
    /// </remarks>
    public Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            var count = queue.Entries.Values.Count(e => e.VisibleAt <= _timeProvider.GetUtcNow().DateTime);
            return Task.FromResult((long)count);
        }

        return Task.FromResult(0L);
    }

    /// <summary>
    /// Creates a new queue with the specified configuration options.
    /// </summary>
    /// <param name="queueName">The name of the queue to create</param>
    /// <param name="options">Optional queue configuration including visibility timeout and max dequeue count</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was created; false if a queue with the same name already exists</returns>
    /// <remarks>
    /// Queue options control behavior such as:
    /// - VisibilityTimeout: How long dequeued messages remain invisible (default 1 minute)
    /// - MaxDequeueCount: Maximum times a message can be dequeued before being considered poison (default 10)
    ///
    /// If options is null, default values are used.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null or empty</exception>
    public Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        var queue = new Queue { Options = options };
        return Task.FromResult(_queues.TryAdd(queueName, queue));
    }

    /// <summary>
    /// Deletes a queue and all its messages.
    /// </summary>
    /// <param name="queueName">The name of the queue to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was deleted; false if the queue does not exist</returns>
    /// <remarks>
    /// WARNING: This operation is destructive and cannot be undone. All messages in the queue
    /// are permanently lost. Use with caution.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null or empty</exception>
    public Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.TryRemove(queueName, out _));
    }

    /// <summary>
    /// Gets the names of all existing queues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue names</returns>
    /// <remarks>
    /// Use this for administration, monitoring, or dynamic queue discovery scenarios.
    /// </remarks>
    public Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.Keys.AsEnumerable());
    }

    /// <summary>
    /// Checks whether a queue with the specified name exists.
    /// </summary>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue exists; otherwise false</returns>
    public Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_queues.ContainsKey(queueName));
    }

    /// <summary>
    /// Internal queue data structure containing entries and configuration.
    /// </summary>
    private class Queue
    {
        /// <summary>
        /// Gets the concurrent dictionary containing all queue entries indexed by entry ID.
        /// </summary>
        public ConcurrentDictionary<string, QueueEntry> Entries { get; } = new();

        /// <summary>
        /// Gets or sets the queue configuration options.
        /// </summary>
        public QueueOptions? Options { get; set; }
    }
}
