using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Provides metrics about processor performance.
/// </summary>
public interface IProcessorMetrics
{
    /// <summary>
    /// Gets the total number of successfully processed messages.
    /// </summary>
    long ProcessedCount { get; }

    /// <summary>
    /// Gets the total number of failed message processing attempts.
    /// </summary>
    long FailedCount { get; }

    /// <summary>
    /// Gets the average processing duration per message.
    /// </summary>
    TimeSpan AverageDuration { get; }
}

/// <summary>
/// Processor for handling commands.
/// </summary>
public interface ICommandProcessor : IProcessor
{
    /// <summary>
    /// Sends a fire-and-forget command to its handler.
    /// </summary>
    /// <param name="command">The command to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command and awaits its response.
    /// </summary>
    /// <typeparam name="TResponse">The response type</typeparam>
    /// <param name="command">The command to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The command response</returns>
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current processor metrics.
    /// </summary>
    /// <returns>Processor metrics</returns>
    IProcessorMetrics GetMetrics();
}

/// <summary>
/// Processor for handling queries.
/// </summary>
public interface IQueryProcessor : IProcessor
{
    /// <summary>
    /// Sends a query and awaits its response.
    /// </summary>
    /// <typeparam name="TResponse">The response type</typeparam>
    /// <param name="query">The query to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The query response</returns>
    Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current processor metrics.
    /// </summary>
    /// <returns>Query processor metrics including cache statistics</returns>
    IQueryProcessorMetrics GetMetrics();
}

/// <summary>
/// Metrics specific to event bus operations.
/// </summary>
public interface IEventBusMetrics
{
    /// <summary>
    /// Gets the total number of events published.
    /// </summary>
    long PublishedCount { get; }

    /// <summary>
    /// Gets the total number of failed event publications.
    /// </summary>
    long FailedCount { get; }

    /// <summary>
    /// Gets the number of registered event handlers.
    /// </summary>
    int RegisteredHandlers { get; }
}

/// <summary>
/// Metrics specific to query processing including cache performance.
/// </summary>
public interface IQueryProcessorMetrics : IProcessorMetrics
{
    /// <summary>
    /// Gets the cache hit rate as a ratio (0.0 to 1.0).
    /// </summary>
    double CacheHitRate { get; }
}

/// <summary>
/// Metrics for queue processing operations.
/// </summary>
public interface IQueueProcessorMetrics
{
    /// <summary>
    /// Gets the total number of messages ever enqueued.
    /// </summary>
    long TotalMessages { get; }

    /// <summary>
    /// Gets the number of successfully processed messages.
    /// </summary>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the number of failed message processing attempts.
    /// </summary>
    long FailedMessages { get; }
}

/// <summary>
/// Metrics for outbox processing operations.
/// </summary>
public interface IOutboxProcessorMetrics
{
    /// <summary>
    /// Gets the number of messages pending delivery.
    /// </summary>
    long PendingMessages { get; }

    /// <summary>
    /// Gets the number of successfully delivered messages.
    /// </summary>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the number of failed delivery attempts.
    /// </summary>
    long FailedMessages { get; }

    /// <summary>
    /// Gets when the last message was successfully processed.
    /// </summary>
    DateTimeOffset? LastProcessedTime { get; }
}

/// <summary>
/// Metrics for inbox processing operations.
/// </summary>
public interface IInboxProcessorMetrics
{
    /// <summary>
    /// Gets the number of successfully processed incoming messages.
    /// </summary>
    long ProcessedMessages { get; }

    /// <summary>
    /// Gets the number of duplicate messages detected.
    /// </summary>
    long DuplicateMessages { get; }

    /// <summary>
    /// Gets the number of failed processing attempts.
    /// </summary>
    long FailedMessages { get; }

    /// <summary>
    /// Gets the deduplication rate as a ratio (0.0 to 1.0).
    /// </summary>
    double DeduplicationRate { get; }
}

/// <summary>
/// Base interface for all processors.
/// </summary>
public interface IProcessor
{
    /// <summary>
    /// Gets whether the processor is currently running.
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Processor for queue-based message processing.
/// </summary>
public interface IQueueProcessor : IProcessor
{
    /// <summary>
    /// Gets the current queue processor metrics.
    /// </summary>
    /// <returns>Queue processor metrics</returns>
    IQueueProcessorMetrics GetMetrics();

    /// <summary>
    /// Gets the names of all active queues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of active queue names</returns>
    Task<IEnumerable<string>> GetActiveQueuesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a message to a specific queue.
    /// </summary>
    /// <param name="message">The message to enqueue</param>
    /// <param name="queueName">The target queue name</param>
    /// <param name="options">Optional enqueue options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts processing messages from a queue.
    /// </summary>
    /// <param name="queueName">The queue to start processing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing messages from a queue.
    /// </summary>
    /// <param name="queueName">The queue to stop processing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current depth of a queue.
    /// </summary>
    /// <param name="queueName">The queue name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The number of messages in the queue</returns>
    Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Processor for outbox pattern message delivery.
/// </summary>
public interface IOutboxProcessor : IProcessor
{
    /// <summary>
    /// Gets the current outbox processor metrics.
    /// </summary>
    /// <returns>Outbox processor metrics</returns>
    IOutboxProcessorMetrics GetMetrics();

    /// <summary>
    /// Publishes a message to the outbox for reliable delivery.
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <param name="options">Optional outbox options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the outbox processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the outbox processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Processor for inbox pattern idempotent message processing.
/// </summary>
public interface IInboxProcessor : IProcessor
{
    /// <summary>
    /// Gets the current inbox processor metrics.
    /// </summary>
    /// <returns>Inbox processor metrics</returns>
    IInboxProcessorMetrics GetMetrics();

    /// <summary>
    /// Processes an incoming message through the inbox.
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="options">Optional inbox options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was processed; false if it was a duplicate</returns>
    Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the inbox processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the inbox processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unprocessed messages in the inbox.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The count of unprocessed messages</returns>
    Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default);
}
