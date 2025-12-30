using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Storage abstraction for message queues with enqueue/dequeue operations.
/// </summary>
public interface IQueueStorage
{
    /// <summary>
    /// Adds a message to the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="message">The message to enqueue</param>
    /// <param name="options">Optional enqueue configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created queue entry</returns>
    Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the next message from the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The dequeued entry, or null if the queue is empty</returns>
    Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns messages from the queue without removing them.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="count">Number of messages to peek. Default: 1.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue entries</returns>
    Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges successful processing of a message.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="entryId">The entry ID to acknowledge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was acknowledged; otherwise, false</returns>
    Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a message, optionally requeuing it.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="entryId">The entry ID to reject</param>
    /// <param name="requeue">Whether to requeue the message. Default: false.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the entry was rejected; otherwise, false</returns>
    Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of messages in the queue.
    /// </summary>
    /// <param name="queueName">The name of the queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of messages in the queue</returns>
    Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new queue with the specified options.
    /// </summary>
    /// <param name="queueName">The name of the queue to create</param>
    /// <param name="options">Optional queue configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was created; false if it already exists</returns>
    Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a queue and all its messages.
    /// </summary>
    /// <param name="queueName">The name of the queue to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue was deleted; false if it didn't exist</returns>
    Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the names of all queues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of queue names</returns>
    Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a queue exists.
    /// </summary>
    /// <param name="queueName">The name of the queue to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the queue exists; otherwise, false</returns>
    Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an entry in a message queue.
/// </summary>
public class QueueEntry
{
    /// <summary>
    /// Unique identifier of the queue entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The message in the queue.
    /// </summary>
    public IMessage Message { get; set; } = null!;

    /// <summary>
    /// Options that were applied when the message was enqueued.
    /// </summary>
    public EnqueueOptions Options { get; set; } = new();

    /// <summary>
    /// When the message was enqueued.
    /// </summary>
    public DateTimeOffset EnqueuedAt { get; set; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// When the message becomes visible for dequeuing (for delayed messages).
    /// </summary>
    public DateTimeOffset? VisibleAt { get; set; }

    /// <summary>
    /// Number of times this message has been dequeued.
    /// </summary>
    public int DequeueCount { get; set; }
}

/// <summary>
/// Configuration options for creating a queue.
/// </summary>
public class QueueOptions
{
    /// <summary>
    /// Maximum number of messages the queue can hold. Null for unlimited.
    /// </summary>
    public int? MaxSize { get; set; }

    /// <summary>
    /// Time-to-live for messages in the queue before automatic removal.
    /// </summary>
    public TimeSpan? MessageTtl { get; set; }

    /// <summary>
    /// Maximum times a message can be dequeued before being dead-lettered.
    /// </summary>
    public int? MaxDequeueCount { get; set; }

    /// <summary>
    /// How long a dequeued message remains invisible before being requeued.
    /// </summary>
    public TimeSpan? VisibilityTimeout { get; set; }

    /// <summary>
    /// Whether to enable priority-based message ordering. Default: true.
    /// </summary>
    public bool EnablePriority { get; set; } = true;
}
