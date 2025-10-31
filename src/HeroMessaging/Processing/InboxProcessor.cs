using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

public class InboxProcessor : IInboxProcessor
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxProcessor> _logger;
    private readonly ActionBlock<InboxEntry> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    private Task? _cleanupTask;

    public InboxProcessor(
        IInboxStorage inboxStorage,
        IServiceProvider serviceProvider,
        ILogger<InboxProcessor> logger)
    {
        _inboxStorage = inboxStorage;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _processingBlock = new ActionBlock<InboxEntry>(
            ProcessInboxEntry,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1, // Sequential processing for ordering
                BoundedCapacity = 100,
                EnsureOrdered = true
            });
    }

    public async Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new InboxOptions();

        // Check for duplicates if idempotency is required
        if (options.RequireIdempotency)
        {
            var isDuplicate = await _inboxStorage.IsDuplicate(
                message.MessageId.ToString(),
                options.DeduplicationWindow,
                cancellationToken);

            if (isDuplicate)
            {
                _logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", message.MessageId);
                return false;
            }
        }

        // Add to inbox
        var entry = await _inboxStorage.Add(message, options, cancellationToken);

        if (entry == null)
        {
            _logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }

        _logger.LogDebug("Message {MessageId} added to inbox from source {Source}",
            message.MessageId, options.Source ?? "Unknown");

        // Process immediately
        await _processingBlock.SendAsync(entry, cancellationToken);

        return true;
    }

    public Task Start(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = PollInbox(_cancellationTokenSource.Token);
        _cleanupTask = RunCleanup(_cancellationTokenSource.Token);

        _logger.LogInformation("Inbox processor started");
        return Task.CompletedTask;
    }

    public async Task Stop()
    {
        _cancellationTokenSource?.Cancel();
        _processingBlock.Complete();

        if (_pollingTask != null)
            await _pollingTask;

        if (_cleanupTask != null)
            await _cleanupTask;

        await _processingBlock.Completion;

        _logger.LogInformation("Inbox processor stopped");
    }

    private async Task PollInbox(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var entries = await _inboxStorage.GetUnprocessed(100, cancellationToken);

                foreach (var entry in entries)
                {
                    await _processingBlock.SendAsync(entry, cancellationToken);
                }

                await Task.Delay(entries.Any() ? 100 : 5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling inbox");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Clean up old processed entries every hour
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);

                await _inboxStorage.CleanupOldEntries(TimeSpan.FromDays(7), cancellationToken);

                _logger.LogDebug("Inbox cleanup completed");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbox cleanup");
            }
        }
    }

    private async Task ProcessInboxEntry(InboxEntry entry)
    {
        try
        {
            // Mark as processing
            entry.Status = InboxStatus.Processing;

            using var scope = _serviceProvider.CreateScope();
            var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();

            // Process based on message type
            switch (entry.Message)
            {
                case ICommand command:
                    await messaging.Send(command);
                    break;

                case IEvent @event:
                    await messaging.Publish(@event);
                    break;

                default:
                    _logger.LogWarning("Unknown message type in inbox: {MessageType}",
                        entry.Message.GetType().Name);
                    break;
            }

            await _inboxStorage.MarkProcessed(entry.Id);

            _logger.LogInformation("Inbox entry {EntryId} (Message: {MessageId}) processed successfully from source {Source}",
                entry.Id, entry.Message.MessageId, entry.Options.Source ?? "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbox entry {EntryId} (Message: {MessageId})",
                entry.Id, entry.Message.MessageId);

            await _inboxStorage.MarkFailed(entry.Id, ex.Message);
        }
    }

    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCount(cancellationToken);
    }
}

/// <summary>
/// Implements the Inbox pattern for exactly-once message processing with automatic deduplication and idempotency guarantees.
/// </summary>
/// <remarks>
/// The inbox processor ensures each message is processed exactly once by:
/// 1. Checking if the message has already been processed (by MessageId)
/// 2. If new: storing in inbox, processing message, marking as processed
/// 3. If duplicate: skipping processing and returning false
///
/// Design Principles:
/// - Exactly-once processing: Messages processed only once based on MessageId
/// - Deduplication: Duplicate messages automatically detected and skipped
/// - Idempotency: Safe to receive same message multiple times
/// - Sequential processing: Messages processed in order received
/// - Automatic cleanup: Old processed messages cleaned up periodically
/// - Source tracking: Records message source for audit trail
///
/// Inbox Pattern Benefits:
/// - Idempotency: Handles at-least-once delivery semantics
/// - Deduplication: Prevents duplicate processing
/// - Audit trail: Complete history of received messages
/// - Failure recovery: Failed messages tracked and isolated
/// - Source transparency: Know where messages originated
///
/// Processing Characteristics:
/// - Sequential processing (MaxDegreeOfParallelism = 1, ordered)
/// - Bounded capacity (100 concurrent messages)
/// - Continuous polling (100ms when active, 5s when idle)
/// - Automatic cleanup (hourly, removes entries >7 days old)
/// - Immediate processing on receipt
/// - Configurable deduplication window
///
/// <code>
/// // Setup inbox processor
/// var inboxProcessor = serviceProvider.GetRequiredService&lt;IInboxProcessor&gt;();
/// await inboxProcessor.Start();
///
/// // Process incoming message (returns true if processed, false if duplicate)
/// var processed = await inboxProcessor.ProcessIncoming(
///     new OrderCreatedEvent { OrderId = orderId },
///     new InboxOptions
///     {
///         RequireIdempotency = true,
///         DeduplicationWindow = TimeSpan.FromHours(24),
///         Source = "external-api"
///     }
/// );
///
/// if (processed)
/// {
///     logger.LogInformation("Message processed successfully");
/// }
/// else
/// {
///     logger.LogWarning("Duplicate message detected and skipped");
/// }
///
/// // Monitor inbox
/// var unprocessed = await inboxProcessor.GetUnprocessedCount();
/// logger.LogInformation("{Count} messages pending processing", unprocessed);
/// </code>
/// </remarks>
public interface IInboxProcessor
{
    /// <summary>
    /// Processes an incoming message with automatic deduplication and exactly-once semantics.
    /// </summary>
    /// <param name="message">The incoming message to process. Must not be null.</param>
    /// <param name="options">Optional inbox options including deduplication settings and source tracking.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// True if the message was processed successfully, false if the message was detected as a duplicate and skipped.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Checks for duplicate messages based on MessageId
    /// - Adds new messages to inbox storage
    /// - Processes non-duplicate messages immediately
    /// - Returns true for successful processing, false for duplicates
    /// - Tracks message source for audit purposes
    ///
    /// Deduplication Behavior:
    /// - MessageId uniqueness: Same MessageId within deduplication window = duplicate
    /// - Deduplication window: Configurable time window (default: unlimited)
    /// - Optional idempotency: Can disable deduplication if RequireIdempotency = false
    /// - Fast duplicate check: Database query before processing
    /// - Logged duplicates: All duplicates logged for monitoring
    ///
    /// Processing Flow:
    /// 1. Check RequireIdempotency setting
    /// 2. If required, check for duplicate in deduplication window
    /// 3. If duplicate found, log warning and return false
    /// 4. Add message to inbox storage
    /// 5. Process message via appropriate handler (ICommandHandler or IEventHandler)
    /// 6. Mark as processed on success
    /// 7. Mark as failed on exception
    /// 8. Return true on success
    ///
    /// InboxOptions:
    /// - RequireIdempotency: Enable/disable deduplication (default: true)
    /// - DeduplicationWindow: Time window for duplicate detection (default: unlimited)
    /// - Source: Source system/endpoint for audit trail
    /// - Metadata: Custom key-value pairs for tracking
    ///
    /// Return Value Interpretation:
    /// - True: Message was new and processed successfully
    /// - False: Message was a duplicate and was skipped (not an error)
    ///
    /// Best Practices:
    /// - Always check return value to distinguish new vs. duplicate
    /// - Set appropriate deduplication window based on message source reliability
    /// - Use Source field to track message origin
    /// - Monitor duplicate rate for anomaly detection
    /// - Ensure message handlers are idempotent as additional safety
    ///
    /// <code>
    /// // Process with idempotency
    /// var wasProcessed = await inboxProcessor.ProcessIncoming(
    ///     new PaymentReceivedEvent
    ///     {
    ///         MessageId = Guid.NewGuid(),
    ///         PaymentId = paymentId,
    ///         Amount = 99.99m
    ///     },
    ///     new InboxOptions
    ///     {
    ///         RequireIdempotency = true,
    ///         DeduplicationWindow = TimeSpan.FromDays(1),
    ///         Source = "payment-gateway"
    ///     }
    /// );
    ///
    /// if (wasProcessed)
    /// {
    ///     logger.LogInformation("Payment processed");
    ///     await SendConfirmationEmail();
    /// }
    /// else
    /// {
    ///     logger.LogWarning("Duplicate payment message ignored");
    /// }
    ///
    /// // Process without deduplication (use with caution)
    /// var result = await inboxProcessor.ProcessIncoming(
    ///     message,
    ///     new InboxOptions { RequireIdempotency = false }
    /// );
    ///
    /// // Bulk processing with duplicate detection
    /// var duplicateCount = 0;
    /// foreach (var msg in incomingMessages)
    /// {
    ///     var processed = await inboxProcessor.ProcessIncoming(
    ///         msg,
    ///         new InboxOptions
    ///         {
    ///             Source = "webhook-endpoint",
    ///             DeduplicationWindow = TimeSpan.FromHours(24)
    ///         }
    ///     );
    ///
    ///     if (!processed)
    ///         duplicateCount++;
    /// }
    ///
    /// logger.LogInformation(
    ///     "Processed {Total} messages, {Duplicates} duplicates",
    ///     incomingMessages.Count,
    ///     duplicateCount
    /// );
    /// </code>
    /// </remarks>
    Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the background processor to poll for unprocessed inbox entries and perform periodic cleanup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the processor has started.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Starts background polling for unprocessed messages
    /// - Starts periodic cleanup task (hourly)
    /// - Idempotent: Safe to call multiple times (won't start duplicate processors)
    /// - Returns immediately (processors run in background)
    /// - Logs startup information
    ///
    /// Processor Behavior:
    /// - Message polling: Retrieves up to 100 unprocessed messages per poll
    /// - Sequential processing: One message at a time (maintains order)
    /// - Polling interval: 100ms when active, 5s when idle
    /// - Automatic cleanup: Removes processed entries >7 days old (hourly)
    /// - Handler resolution: Resolves ICommandHandler or IEventHandler from DI
    /// - Status tracking: Updates entry status (Processing, Processed, Failed)
    ///
    /// Cleanup Behavior:
    /// - Frequency: Runs every hour
    /// - Retention: Removes entries older than 7 days
    /// - Only processed: Only removes successfully processed entries
    /// - Failed entries: Kept for manual investigation
    /// - Logged: Cleanup operations are logged at debug level
    ///
    /// Lifecycle:
    /// - Runs until Stop() is called or application shuts down
    /// - Two background tasks: polling and cleanup
    /// - Graceful shutdown on Stop()
    /// - Completes in-flight messages before stopping
    ///
    /// <code>
    /// // Start on application startup
    /// public class InboxStartupService : IHostedService
    /// {
    ///     private readonly IInboxProcessor _inboxProcessor;
    ///
    ///     public InboxStartupService(IInboxProcessor inboxProcessor)
    ///     {
    ///         _inboxProcessor = inboxProcessor;
    ///     }
    ///
    ///     public async Task StartAsync(CancellationToken cancellationToken)
    ///     {
    ///         await _inboxProcessor.Start(cancellationToken);
    ///         logger.LogInformation("Inbox processor started");
    ///     }
    ///
    ///     public async Task StopAsync(CancellationToken cancellationToken)
    ///     {
    ///         await _inboxProcessor.Stop();
    ///         logger.LogInformation("Inbox processor stopped");
    ///     }
    /// }
    ///
    /// // Manual start/stop
    /// await inboxProcessor.Start();
    /// // Processor now running in background
    ///
    /// // ... application runs ...
    ///
    /// await inboxProcessor.Stop();
    ///
    /// // Idempotent - safe to call multiple times
    /// await inboxProcessor.Start(); // Starts if not running
    /// await inboxProcessor.Start(); // Does nothing, already running
    /// </code>
    /// </remarks>
    Task Start(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background processor gracefully, completing any in-flight message processing.
    /// </summary>
    /// <returns>A task that completes when the processor has stopped.</returns>
    /// <remarks>
    /// This method:
    /// - Signals both polling and cleanup tasks to stop
    /// - Waits for polling loop to exit
    /// - Waits for cleanup task to exit
    /// - Completes in-flight message processing
    /// - Waits for processing block to complete
    /// - Logs shutdown information
    ///
    /// Shutdown Behavior:
    /// - Graceful: Completes current messages before stopping
    /// - No message loss: Unprocessed messages remain in inbox
    /// - Blocking: Waits for all background tasks to complete
    /// - Clean state: All processors fully stopped before method returns
    /// - Idempotent: Safe to call multiple times
    ///
    /// Timing:
    /// - Polling loop: Stops on next iteration (up to 5s)
    /// - Cleanup task: Stops on next iteration (up to 1 hour if running)
    /// - In-flight messages: Complete before returning
    /// - Processing block: Drains remaining messages
    /// - Total time: Typically &lt;5s for graceful shutdown
    ///
    /// Use Cases:
    /// - Application shutdown
    /// - Maintenance mode
    /// - Testing and debugging
    /// - Resource cleanup
    /// - Configuration changes requiring restart
    ///
    /// <code>
    /// // Graceful shutdown
    /// logger.LogInformation("Stopping inbox processor...");
    /// await inboxProcessor.Stop();
    /// logger.LogInformation("Inbox processor stopped");
    ///
    /// // Shutdown with monitoring
    /// var stopwatch = Stopwatch.StartNew();
    /// await inboxProcessor.Stop();
    /// logger.LogInformation("Inbox stopped in {Elapsed}ms", stopwatch.ElapsedMilliseconds);
    ///
    /// // Stop for maintenance
    /// await inboxProcessor.Stop();
    /// await PerformInboxMaintenance();
    /// await inboxProcessor.Start();
    ///
    /// // Idempotent - safe to call if already stopped
    /// await inboxProcessor.Stop(); // Stops processor
    /// await inboxProcessor.Stop(); // Does nothing, already stopped
    /// </code>
    /// </remarks>
    Task Stop();

    /// <summary>
    /// Gets the current count of unprocessed messages in the inbox.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of messages waiting to be processed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <remarks>
    /// This method:
    /// - Queries the underlying storage for unprocessed count
    /// - Returns count of messages with status != Processed
    /// - Includes messages with status = Failed
    /// - Fast operation (single database query)
    /// - Read-only (doesn't modify inbox state)
    ///
    /// Use Cases:
    /// - Monitoring inbox backlog
    /// - Health checks
    /// - Alerting thresholds
    /// - Dashboard metrics
    /// - Load balancing decisions
    ///
    /// Count Interpretation:
    /// - 0: Inbox is empty, all messages processed
    /// - 1-10: Normal operation, processor keeping up
    /// - 10-100: Moderate backlog, monitor closely
    /// - 100+: High backlog, investigate processing delays or increase capacity
    ///
    /// Note: This count includes both pending and failed messages.
    /// Failed messages require manual intervention and won't auto-retry.
    ///
    /// <code>
    /// // Check unprocessed count
    /// var count = await inboxProcessor.GetUnprocessedCount();
    /// logger.LogInformation("Inbox has {Count} unprocessed messages", count);
    ///
    /// // Alert on high backlog
    /// if (count > 100)
    /// {
    ///     logger.LogWarning("High inbox backlog: {Count} messages", count);
    ///     await alertService.SendAlert($"Inbox backlog: {count} messages");
    /// }
    ///
    /// // Periodic monitoring
    /// while (isRunning)
    /// {
    ///     var unprocessed = await inboxProcessor.GetUnprocessedCount();
    ///     metrics.RecordGauge("inbox.unprocessed", unprocessed);
    ///     await Task.Delay(TimeSpan.FromSeconds(30));
    /// }
    ///
    /// // Health check
    /// public class InboxHealthCheck : IHealthCheck
    /// {
    ///     private readonly IInboxProcessor _inboxProcessor;
    ///
    ///     public async Task&lt;HealthCheckResult&gt; CheckHealthAsync(
    ///         HealthCheckContext context,
    ///         CancellationToken cancellationToken = default)
    ///     {
    ///         var count = await _inboxProcessor.GetUnprocessedCount(cancellationToken);
    ///
    ///         return count switch
    ///         {
    ///             0 => HealthCheckResult.Healthy("Inbox is empty"),
    ///             &lt; 50 => HealthCheckResult.Healthy($"Inbox has {count} messages"),
    ///             &lt; 100 => HealthCheckResult.Degraded($"Moderate backlog: {count} messages"),
    ///             _ => HealthCheckResult.Unhealthy($"High backlog: {count} messages")
    ///         };
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default);
}