using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Main interface for the HeroMessaging system. Provides unified access to CQRS operations,
/// event publishing, queuing, and transactional messaging patterns (Inbox/Outbox).
/// </summary>
/// <remarks>
/// This is the primary entry point for interacting with HeroMessaging.
/// It supports:
/// - CQRS: Commands, Queries, Events
/// - Queuing: Background job processing
/// - Outbox Pattern: Transactional message publishing
/// - Inbox Pattern: Exactly-once message processing
/// - Observability: Metrics and health checks
///
/// Register in DI container:
/// <code>
/// services.AddHeroMessaging(builder =>
/// {
///     builder.UseInMemoryStorage()
///            .UseJsonSerialization()
///            .UseInMemoryTransport();
/// });
/// </code>
///
/// Basic usage:
/// <code>
/// // Commands
/// await messaging.Send(new CreateOrderCommand("CUST-001", 99.99m));
/// var response = await messaging.Send(new CreateOrderCommand("CUST-001", 99.99m));
///
/// // Queries
/// var order = await messaging.Send(new GetOrderByIdQuery("ORDER-001"));
///
/// // Events
/// await messaging.Publish(new OrderCreatedEvent("ORDER-001", "CUST-001", 99.99m));
///
/// // Queuing
/// await messaging.Enqueue(message, "email-queue");
/// await messaging.StartQueue("email-queue");
/// </code>
/// </remarks>
public interface IHeroMessaging
{
    /// <summary>
    /// Sends a command that does not return a response.
    /// Commands are processed synchronously by exactly one handler.
    /// </summary>
    /// <param name="command">The command to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when command is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type</exception>
    /// <exception cref="ValidationException">Thrown when command validation fails</exception>
    Task Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command that returns a typed response.
    /// Commands are processed synchronously by exactly one handler.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the command</typeparam>
    /// <param name="command">The command to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing the command response</returns>
    /// <exception cref="ArgumentNullException">Thrown when command is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the command type</exception>
    /// <exception cref="ValidationException">Thrown when command validation fails</exception>
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a query that returns data without modifying state.
    /// Queries are processed synchronously by exactly one handler and must be idempotent.
    /// </summary>
    /// <typeparam name="TResponse">The type of data returned by the query</typeparam>
    /// <param name="query">The query to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task containing the query result</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the query type</exception>
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to all registered event handlers.
    /// Events are fire-and-forget and can have zero or more handlers.
    /// </summary>
    /// <param name="event">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Events are published asynchronously. Individual handler failures do not stop other handlers from processing.
    /// Use <see cref="PublishToOutbox"/> if you need transactional guarantees.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when event is null</exception>
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a message for background processing.
    /// Messages are processed by workers when the queue is started.
    /// </summary>
    /// <param name="message">The message to enqueue</param>
    /// <param name="queueName">Name of the queue</param>
    /// <param name="options">Optional enqueue configuration (priority, delay, metadata)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Use queues for:
    /// - Background job processing
    /// - Rate-limited operations
    /// - Delayed message processing
    /// - Priority-based processing
    ///
    /// Start the queue with <see cref="StartQueue"/> to begin processing messages.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message or queueName is null</exception>
    Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts processing messages from the specified queue.
    /// Workers will continuously poll and process messages until the queue is stopped.
    /// </summary>
    /// <param name="queueName">Name of the queue to start</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when queue is already started</exception>
    Task StartQueue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing messages from the specified queue.
    /// In-flight messages will complete, but no new messages will be processed.
    /// </summary>
    /// <param name="queueName">Name of the queue to stop</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null</exception>
    Task StopQueue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a message in the outbox for transactional publishing.
    /// Messages are published after the current transaction commits, ensuring exactly-once delivery.
    /// </summary>
    /// <param name="message">The message to store in outbox</param>
    /// <param name="options">Optional outbox configuration (destination, priority, retries)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Outbox Pattern ensures that messages are published atomically with database changes:
    /// 1. Store message in outbox table within same transaction as domain changes
    /// 2. Commit transaction
    /// 3. Background processor publishes messages from outbox
    /// 4. Mark messages as published
    ///
    /// This guarantees that messages are only published if the transaction succeeds,
    /// preventing inconsistencies between database state and published events.
    ///
    /// Example:
    /// <code>
    /// using var scope = serviceProvider.CreateScope();
    /// var messaging = scope.ServiceProvider.GetRequiredService&lt;IHeroMessaging&gt;();
    /// var unitOfWork = scope.ServiceProvider.GetRequiredService&lt;IUnitOfWork&gt;();
    ///
    /// await unitOfWork.BeginAsync();
    /// try
    /// {
    ///     // Make database changes
    ///     await repository.SaveOrderAsync(order);
    ///
    ///     // Store event in outbox (same transaction)
    ///     await messaging.PublishToOutbox(new OrderCreatedEvent(order.Id));
    ///
    ///     await unitOfWork.CommitAsync(); // Commits both changes and outbox entry
    /// }
    /// catch
    /// {
    ///     await unitOfWork.RollbackAsync(); // Rolls back everything
    ///     throw;
    /// }
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an incoming message through the inbox pattern for exactly-once delivery.
    /// Deduplicates messages based on MessageId to prevent duplicate processing.
    /// </summary>
    /// <param name="message">The incoming message to process</param>
    /// <param name="options">Optional inbox configuration (source, idempotency, deduplication)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// Inbox Pattern ensures exactly-once message processing:
    /// 1. Check if message was already processed (by MessageId)
    /// 2. If new: Store in inbox, process message, mark as processed (atomic transaction)
    /// 3. If duplicate: Skip processing (idempotent)
    ///
    /// This prevents duplicate processing when messages are delivered multiple times
    /// (common in distributed systems with at-least-once delivery guarantees).
    ///
    /// Example:
    /// <code>
    /// // Receiving from external message broker
    /// await messaging.ProcessIncoming(receivedMessage, new InboxOptions
    /// {
    ///     Source = "rabbitmq",
    ///     RequireIdempotency = true,
    ///     DeduplicationWindow = TimeSpan.FromDays(7)
    /// });
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    Task ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current messaging metrics including message counts, queue depths, and processing times.
    /// </summary>
    /// <returns>Snapshot of current messaging metrics</returns>
    MessagingMetrics GetMetrics();

    /// <summary>
    /// Gets current health status of all messaging components (storage, transport, processors).
    /// </summary>
    /// <returns>Health status of messaging system and all components</returns>
    MessagingHealth GetHealth();
}

/// <summary>
/// Configuration options for enqueueing messages for background processing.
/// </summary>
public class EnqueueOptions
{
    /// <summary>
    /// Gets or sets the priority of the message in the queue.
    /// Higher priority messages are processed before lower priority messages.
    /// Default: 0
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Gets or sets the delay before the message becomes available for processing.
    /// Use for scheduled or delayed message processing.
    /// Default: null (no delay, process immediately)
    /// </summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>
    /// Gets or sets additional metadata to attach to the message.
    /// Useful for tagging, routing, or tracking messages.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Configuration options for the Outbox pattern.
/// Controls how messages are stored and published transactionally.
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// Gets or sets the destination address or topic where the message will be published.
    /// If null, uses default routing based on message type.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// Gets or sets the priority for publishing this message from the outbox.
    /// Higher priority messages are published before lower priority messages.
    /// Default: 0
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts if publishing fails.
    /// After max retries, message moves to dead-letter queue.
    /// Default: 3
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts.
    /// If null, uses exponential backoff strategy.
    /// </summary>
    public TimeSpan? RetryDelay { get; set; }
}

/// <summary>
/// Configuration options for the Inbox pattern.
/// Controls deduplication and exactly-once processing behavior.
/// </summary>
public class InboxOptions
{
    /// <summary>
    /// Gets or sets the source system or broker that sent this message.
    /// Used for tracking and diagnostics.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets whether to enforce idempotency (exactly-once processing).
    /// When true, duplicate messages (same MessageId) are automatically skipped.
    /// Default: true
    /// </summary>
    public bool RequireIdempotency { get; set; } = true;

    /// <summary>
    /// Gets or sets how long to keep message IDs for deduplication.
    /// Messages older than this window can be processed again.
    /// If null, uses system default (typically 7 days).
    /// </summary>
    public TimeSpan? DeduplicationWindow { get; set; }
}

/// <summary>
/// Provides metrics and statistics about messaging operations.
/// All counts are cumulative since system start.
/// </summary>
public class MessagingMetrics
{
    /// <summary>
    /// Gets or sets the total number of commands sent.
    /// </summary>
    public long CommandsSent { get; set; }

    /// <summary>
    /// Gets or sets the total number of queries sent.
    /// </summary>
    public long QueriesSent { get; set; }

    /// <summary>
    /// Gets or sets the total number of events published.
    /// </summary>
    public long EventsPublished { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages enqueued.
    /// </summary>
    public long MessagesQueued { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages stored in outbox.
    /// </summary>
    public long OutboxMessages { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages processed through inbox.
    /// </summary>
    public long InboxMessages { get; set; }

    /// <summary>
    /// Gets or sets the current depth (pending message count) for each queue.
    /// Key: queue name, Value: number of pending messages
    /// </summary>
    public Dictionary<string, long> QueueDepths { get; set; } = new();

    /// <summary>
    /// Gets or sets the average processing time in milliseconds for each message type.
    /// Key: message type name, Value: average processing time (ms)
    /// </summary>
    public Dictionary<string, double> AverageProcessingTime { get; set; } = new();
}

/// <summary>
/// Represents the health status of the messaging system and all its components.
/// </summary>
public class MessagingHealth
{
    /// <summary>
    /// Gets or sets whether the overall messaging system is healthy.
    /// False if any critical component is unhealthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Gets or sets the health status of individual components (storage, transport, processors).
    /// Key: component name, Value: component health status
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}

/// <summary>
/// Represents the health status of a single messaging component.
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// Gets or sets whether this component is healthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Gets or sets the current status description (e.g., "Healthy", "Degraded", "Unhealthy").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets additional diagnostic information or error messages.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets when this component was last checked.
    /// </summary>
    public DateTime LastChecked { get; set; }
}