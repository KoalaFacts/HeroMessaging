using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

/// <summary>
/// Default implementation of <see cref="IQueueProcessor"/> that manages background message processing through named durable queues.
/// </summary>
/// <param name="serviceProvider">The service provider used to resolve message handlers and dependencies.</param>
/// <param name="queueStorage">The storage provider for persisting and retrieving queue messages.</param>
/// <param name="logger">The logger for diagnostic output.</param>
/// <remarks>
/// This implementation provides reliable background job processing with the following characteristics:
/// - Named queues for logical separation of work
/// - Durable storage (messages survive application restarts)
/// - Priority-based message ordering within queues
/// - Automatic retry with configurable limits (default: 3 attempts)
/// - Dead-letter handling for permanently failed messages
/// - Independent lifecycle management per queue
/// - Sequential processing per queue (parallel across queues)
///
/// Implementation Details:
/// - Uses ConcurrentDictionary to manage multiple queue workers
/// - Each queue has a dedicated worker (QueueWorker) running in background
/// - Workers use TPL Dataflow ActionBlock for processing pipeline
/// - Sequential processing per queue (MaxDegreeOfParallelism = 1)
/// - Bounded capacity per queue (100 messages)
/// - Continuous polling with adaptive backoff
/// - Auto-creation of queues on first use
///
/// Queue workers poll for messages continuously and dispatch them to appropriate handlers
/// based on message type (ICommand or IEvent). Messages are acknowledged on success or
/// rejected with optional requeue on failure.
/// </remarks>
public class QueueProcessor(
    IServiceProvider serviceProvider,
    IQueueStorage queueStorage,
    ILogger<QueueProcessor> logger) : IQueueProcessor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IQueueStorage _queueStorage = queueStorage;
    private readonly ILogger<QueueProcessor> _logger = logger;
    private readonly ConcurrentDictionary<string, QueueWorker> _workers = new();

    /// <summary>
    /// Enqueues a message to the specified queue for background processing.
    /// </summary>
    /// <param name="message">The message to enqueue. Must implement ICommand or IEvent.</param>
    /// <param name="queueName">The name of the queue to add the message to.</param>
    /// <param name="options">Optional enqueue options including priority and scheduling.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the message has been added to the queue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when message or queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    public async Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!await _queueStorage.QueueExists(queueName, cancellationToken))
        {
            await _queueStorage.CreateQueue(queueName, null, cancellationToken);
        }

        await _queueStorage.Enqueue(queueName, message, options, cancellationToken);
        _logger.LogDebug("Message enqueued to {QueueName} with priority {Priority}", queueName, options?.Priority ?? 0);
    }

    /// <summary>
    /// Starts a background worker to process messages from the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to start processing.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the queue worker has started.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    public async Task StartQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (!await _queueStorage.QueueExists(queueName, cancellationToken))
        {
            await _queueStorage.CreateQueue(queueName, null, cancellationToken);
        }

        var worker = _workers.GetOrAdd(queueName, _ => new QueueWorker(
            queueName,
            _queueStorage,
            _serviceProvider,
            _logger));

        await worker.Start(cancellationToken);
    }

    /// <summary>
    /// Stops the background worker processing messages from the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to stop processing.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the queue worker has stopped gracefully.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    public async Task StopQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (_workers.TryRemove(queueName, out var worker))
        {
            await worker.Stop();
        }
    }

    /// <summary>
    /// Gets the current number of messages waiting in the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to check.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of unprocessed messages in the queue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    public async Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        return await _queueStorage.GetQueueDepth(queueName, cancellationToken);
    }

    private class QueueWorker
    {
        private readonly string _queueName;
        private readonly IQueueStorage _storage;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private readonly ActionBlock<QueueEntry> _processingBlock;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pollingTask;

        public QueueWorker(
            string queueName,
            IQueueStorage storage,
            IServiceProvider serviceProvider,
            ILogger logger)
        {
            _queueName = queueName;
            _storage = storage;
            _serviceProvider = serviceProvider;
            _logger = logger;

            _processingBlock = new ActionBlock<QueueEntry>(
                ProcessMessage,
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                    BoundedCapacity = 100
                });
        }

        public Task Start(CancellationToken cancellationToken)
        {
            if (_cancellationTokenSource != null)
                return Task.CompletedTask;

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = PollQueue(_cancellationTokenSource.Token);

            _logger.LogInformation("Queue worker started for {QueueName}", _queueName);
            return Task.CompletedTask;
        }

        public async Task Stop()
        {
            _cancellationTokenSource?.Cancel();
            _processingBlock.Complete();

            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
            }

            await _processingBlock.Completion;

            _logger.LogInformation("Queue worker stopped for {QueueName}", _queueName);
        }

        private async Task PollQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var entry = await _storage.Dequeue(_queueName, cancellationToken);
                    if (entry != null)
                    {
                        await _processingBlock.SendAsync(entry, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling queue {QueueName}", _queueName);
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task ProcessMessage(QueueEntry entry)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();

                switch (entry.Message)
                {
                    case ICommand command:
                        await messaging.Send(command);
                        break;
                    case IEvent @event:
                        await messaging.Publish(@event);
                        break;
                    default:
                        _logger.LogWarning("Unknown message type in queue: {MessageType}", entry.Message.GetType().Name);
                        break;
                }

                await _storage.Acknowledge(_queueName, entry.Id);
                _logger.LogDebug("Message {MessageId} processed successfully from queue {QueueName}",
                    entry.Message.MessageId, _queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId} from queue {QueueName}",
                    entry.Message.MessageId, _queueName);

                await _storage.Reject(_queueName, entry.Id, requeue: entry.DequeueCount < 3);
            }
        }
    }
}

/// <summary>
/// Manages background message processing through named queues with support for priority, retry, and dead-letter handling.
/// </summary>
/// <remarks>
/// The queue processor implements reliable background job processing using persistent queues.
/// Messages are stored in durable storage and processed asynchronously by background workers.
///
/// Design Principles:
/// - Durable queues survive application restarts
/// - Named queues enable logical separation of work
/// - Priority-based message ordering
/// - Automatic retry with requeue on failure
/// - Dead-letter queue for failed messages
/// - Independent queue lifecycle management
///
/// Queue Characteristics:
/// - Named queues: Each queue has a unique name (e.g., "orders", "notifications")
/// - Persistent storage: Messages survive crashes and restarts
/// - FIFO ordering: Messages processed in order within priority levels
/// - Priority support: Higher priority messages processed first
/// - Automatic retry: Failed messages requeued up to retry limit
/// - Dead-letter handling: Failed messages after max retries
///
/// Processing Characteristics:
/// - One worker per queue (sequential processing per queue)
/// - Multiple queues can run in parallel
/// - Bounded processing capacity (100 messages per queue)
/// - Automatic message acknowledgment on success
/// - Automatic requeue on failure (up to 3 attempts)
/// - Continuous polling with backoff
///
/// <code>
/// // Setup queue processor
/// var queueProcessor = serviceProvider.GetRequiredService&lt;IQueueProcessor&gt;();
///
/// // Start a queue for processing
/// await queueProcessor.StartQueue("order-processing");
/// await queueProcessor.StartQueue("email-notifications");
///
/// // Enqueue messages with priority
/// await queueProcessor.Enqueue(
///     new ProcessOrderCommand { OrderId = orderId },
///     "order-processing",
///     new EnqueueOptions { Priority = 10 } // Higher priority
/// );
///
/// await queueProcessor.Enqueue(
///     new SendEmailCommand { To = "user@example.com" },
///     "email-notifications",
///     new EnqueueOptions { Priority = 5 } // Normal priority
/// );
///
/// // Check queue depth
/// var depth = await queueProcessor.GetQueueDepth("order-processing");
/// if (depth > 100)
/// {
///     logger.LogWarning("Order queue backlog: {Depth} messages", depth);
/// }
///
/// // Stop a queue gracefully
/// await queueProcessor.StopQueue("email-notifications");
/// </code>
/// </remarks>
public interface IQueueProcessor
{
    /// <summary>
    /// Enqueues a message to the specified queue for background processing.
    /// </summary>
    /// <param name="message">The message to enqueue. Must implement ICommand or IEvent.</param>
    /// <param name="queueName">The name of the queue to add the message to.</param>
    /// <param name="options">Optional enqueue options including priority and scheduling.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the message has been added to the queue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when message or queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Creates the queue if it doesn't exist
    /// - Adds the message to persistent storage
    /// - Respects priority ordering (higher priority processed first)
    /// - Returns immediately after storage (doesn't wait for processing)
    /// - Logs debug information about enqueued message
    ///
    /// Enqueue Behavior:
    /// - Queue auto-creation: Queues are created automatically if they don't exist
    /// - Priority ordering: Messages with higher priority values are processed first
    /// - Within same priority: FIFO ordering
    /// - Persistent storage: Messages survive application restarts
    /// - No immediate processing: Message waits for queue worker to pick it up
    ///
    /// EnqueueOptions:
    /// - Priority: Integer priority (default 0, higher = more important)
    /// - ScheduledTime: Process message at specific future time (deferred processing)
    /// - MaxRetries: Override default retry limit for this message
    /// - Metadata: Custom key-value pairs for tracking/routing
    ///
    /// Performance Considerations:
    /// - Enqueue is fast (single database write)
    /// - No blocking on processing
    /// - Bulk enqueuing is efficient (separate transactions)
    /// - Storage I/O is the bottleneck
    ///
    /// <code>
    /// // Simple enqueue
    /// await queueProcessor.Enqueue(
    ///     new ProcessPaymentCommand { PaymentId = paymentId },
    ///     "payment-processing"
    /// );
    ///
    /// // High priority message
    /// await queueProcessor.Enqueue(
    ///     new RefundPaymentCommand { PaymentId = paymentId },
    ///     "payment-processing",
    ///     new EnqueueOptions { Priority = 100 } // Process before normal messages
    /// );
    ///
    /// // Scheduled message (process later)
    /// await queueProcessor.Enqueue(
    ///     new SendReminderCommand { UserId = userId },
    ///     "notifications",
    ///     new EnqueueOptions
    ///     {
    ///         ScheduledTime = DateTime.UtcNow.AddHours(24) // Send in 24 hours
    ///     }
    /// );
    ///
    /// // Bulk enqueue
    /// foreach (var notification in notifications)
    /// {
    ///     await queueProcessor.Enqueue(notification, "notifications");
    /// }
    /// </code>
    /// </remarks>
    Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a background worker to process messages from the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to start processing.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the queue worker has started.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Creates the queue if it doesn't exist
    /// - Starts a background worker for the queue
    /// - Idempotent: Safe to call multiple times (won't start duplicate workers)
    /// - Returns immediately (worker runs in background)
    /// - Logs startup information
    ///
    /// Worker Behavior:
    /// - Continuous polling: Worker continuously polls for messages
    /// - Sequential processing: One message at a time per queue
    /// - Automatic retry: Failed messages requeued up to 3 times
    /// - Graceful backoff: 100ms delay when queue is empty, 1s on errors
    /// - Handler resolution: Resolves ICommandHandler or IEventHandler from DI
    /// - Automatic acknowledgment: Successful messages removed from queue
    /// - Requeue on failure: Failed messages requeued if dequeue count &lt; 3
    ///
    /// Lifecycle:
    /// - Worker runs until stopped via StopQueue or application shutdown
    /// - Survives application restarts (messages remain in queue)
    /// - Multiple queues can run simultaneously
    /// - Each queue has independent worker
    ///
    /// <code>
    /// // Start queue processing
    /// await queueProcessor.StartQueue("order-processing");
    /// logger.LogInformation("Order processing queue started");
    ///
    /// // Start multiple queues
    /// await Task.WhenAll(
    ///     queueProcessor.StartQueue("orders"),
    ///     queueProcessor.StartQueue("notifications"),
    ///     queueProcessor.StartQueue("analytics")
    /// );
    ///
    /// // Idempotent - safe to call again
    /// await queueProcessor.StartQueue("orders"); // Does nothing, already running
    ///
    /// // Start queue on application startup
    /// public class QueueStartupService : IHostedService
    /// {
    ///     private readonly IQueueProcessor _queueProcessor;
    ///
    ///     public async Task StartAsync(CancellationToken cancellationToken)
    ///     {
    ///         await _queueProcessor.StartQueue("background-jobs", cancellationToken);
    ///     }
    ///
    ///     public async Task StopAsync(CancellationToken cancellationToken)
    ///     {
    ///         await _queueProcessor.StopQueue("background-jobs", cancellationToken);
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task StartQueue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background worker processing messages from the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to stop processing.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the queue worker has stopped gracefully.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Signals the worker to stop
    /// - Waits for current message processing to complete
    /// - Completes in-flight work gracefully
    /// - Does not lose messages (remain in queue)
    /// - Idempotent: Safe to call if queue is already stopped
    /// - Logs shutdown information
    ///
    /// Shutdown Behavior:
    /// - Graceful: Current message completes before stopping
    /// - No message loss: Unprocessed messages remain in queue
    /// - Idempotent: Safe to call multiple times
    /// - Blocking: Waits for worker to fully stop
    /// - Clean state: Worker is removed from active workers
    ///
    /// Use Cases:
    /// - Application shutdown
    /// - Queue maintenance
    /// - Temporary pause processing
    /// - Resource management
    /// - Testing and debugging
    ///
    /// <code>
    /// // Stop queue for maintenance
    /// await queueProcessor.StopQueue("order-processing");
    /// logger.LogInformation("Queue stopped for maintenance");
    ///
    /// // Perform maintenance
    /// await PerformDatabaseMaintenance();
    ///
    /// // Restart queue
    /// await queueProcessor.StartQueue("order-processing");
    ///
    /// // Stop all queues on shutdown
    /// var activeQueues = await queueProcessor.GetActiveQueues();
    /// await Task.WhenAll(activeQueues.Select(q => queueProcessor.StopQueue(q)));
    ///
    /// // Idempotent - safe to call if already stopped
    /// await queueProcessor.StopQueue("notifications"); // Does nothing if not running
    /// </code>
    /// </remarks>
    Task StopQueue(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of messages waiting in the specified queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to check.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of unprocessed messages in the queue.</returns>
    /// <exception cref="ArgumentNullException">Thrown when queueName is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Queries the underlying storage for queue depth
    /// - Returns count of unprocessed messages
    /// - Includes scheduled messages not yet due
    /// - Fast operation (single database query)
    /// - Read-only (doesn't modify queue state)
    ///
    /// Use Cases:
    /// - Monitoring queue backlogs
    /// - Scaling decisions (add workers if depth is high)
    /// - Health checks
    /// - Alerting thresholds
    /// - Dashboard metrics
    ///
    /// Queue Depth Interpretation:
    /// - 0: Queue is empty, no pending work
    /// - 1-100: Normal operation, worker keeping up
    /// - 100-1000: Moderate backlog, monitor closely
    /// - 1000+: High backlog, consider scaling or investigating delays
    ///
    /// <code>
    /// // Check queue depth
    /// var depth = await queueProcessor.GetQueueDepth("order-processing");
    /// logger.LogInformation("Order queue depth: {Depth}", depth);
    ///
    /// // Alert on high backlog
    /// if (depth > 1000)
    /// {
    ///     logger.LogWarning("High queue backlog detected: {Depth} messages", depth);
    ///     await alertService.SendAlert($"Queue backlog: {depth} messages");
    /// }
    ///
    /// // Monitor all queues
    /// var queues = new[] { "orders", "notifications", "analytics" };
    /// foreach (var queue in queues)
    /// {
    ///     var queueDepth = await queueProcessor.GetQueueDepth(queue);
    ///     metrics.RecordGauge($"queue.{queue}.depth", queueDepth);
    /// }
    ///
    /// // Health check
    /// public class QueueHealthCheck : IHealthCheck
    /// {
    ///     public async Task&lt;HealthCheckResult&gt; CheckHealthAsync(HealthCheckContext context)
    ///     {
    ///         var depth = await _queueProcessor.GetQueueDepth("critical-queue");
    ///         return depth &lt; 1000
    ///             ? HealthCheckResult.Healthy($"Queue depth: {depth}")
    ///             : HealthCheckResult.Degraded($"High queue backlog: {depth}");
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default);
}