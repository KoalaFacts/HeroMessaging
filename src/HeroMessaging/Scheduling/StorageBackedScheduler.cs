using System.Threading.Channels;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Scheduling;

/// <summary>
/// Storage-backed message scheduler with persistent state and polling-based delivery.
/// </summary>
/// <remarks>
/// This scheduler is designed for production use with persistent storage. It polls the storage
/// at regular intervals for messages that are due for delivery and processes them concurrently.
///
/// Key features:
/// - Persistent storage survives process restarts
/// - Concurrent message delivery with configurable parallelism
/// - Automatic cleanup of old delivered/cancelled messages
/// - Graceful shutdown with cancellation support
/// </remarks>
public sealed class StorageBackedScheduler : IMessageScheduler, IAsyncDisposable
{
    private readonly IScheduledMessageStorage _storage;
    private readonly IMessageDeliveryHandler _deliveryHandler;
    private readonly StorageBackedSchedulerOptions _options;
    private readonly ILogger<StorageBackedScheduler> _logger;
    private readonly Channel<ScheduledMessage> _deliveryChannel;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly Task _pollingTask;
    private readonly Task _deliveryTask;
    private readonly Task? _cleanupTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageBackedScheduler"/> class.
    /// Starts background workers for polling, delivery, and optional cleanup.
    /// </summary>
    /// <param name="storage">The persistent storage for scheduled messages.</param>
    /// <param name="deliveryHandler">The handler responsible for delivering scheduled messages when they become due.</param>
    /// <param name="options">Configuration options for the scheduler including polling interval and concurrency settings.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public StorageBackedScheduler(
        IScheduledMessageStorage storage,
        IMessageDeliveryHandler deliveryHandler,
        StorageBackedSchedulerOptions options,
        ILogger<StorageBackedScheduler> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _deliveryHandler = deliveryHandler ?? throw new ArgumentNullException(nameof(deliveryHandler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _deliveryChannel = Channel.CreateBounded<ScheduledMessage>(new BoundedChannelOptions(options.BatchSize * 2)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _shutdownCts = new CancellationTokenSource();

        // Start background workers
        _pollingTask = Task.Run(() => PollForDueMessagesAsync(_shutdownCts.Token));
        _deliveryTask = Task.Run(() => ProcessDeliveriesAsync(_shutdownCts.Token));

        if (_options.AutoCleanup)
        {
            _cleanupTask = Task.Run(() => PeriodicCleanupAsync(_shutdownCts.Token));
        }

        _logger.LogInformation(
            "StorageBackedScheduler started with PollingInterval={PollingInterval}ms, BatchSize={BatchSize}, MaxConcurrency={MaxConcurrency}",
            _options.PollingInterval.TotalMilliseconds,
            _options.BatchSize,
            _options.MaxConcurrency);
    }

    /// <inheritdoc />
    public async Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        TimeSpan delay,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative");

        var deliverAt = DateTimeOffset.UtcNow.Add(delay);
        return await ScheduleAsync(message, deliverAt, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        DateTimeOffset deliverAt,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (deliverAt < DateTimeOffset.UtcNow) throw new ArgumentException("Delivery time cannot be in the past", nameof(deliverAt));

        try
        {
            var scheduleId = Guid.NewGuid();
            var scheduledMessage = new ScheduledMessage
            {
                ScheduleId = scheduleId,
                Message = message,
                DeliverAt = deliverAt,
                Options = options ?? new SchedulingOptions(),
                ScheduledAt = DateTimeOffset.UtcNow
            };

            await _storage.AddAsync(scheduledMessage, cancellationToken);

            _logger.LogDebug(
                "Scheduled message {ScheduleId} (Type: {MessageType}) for delivery at {DeliverAt}",
                scheduleId,
                message.GetType().Name,
                deliverAt);

            return ScheduleResult.Successful(scheduleId, deliverAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule message");
            return ScheduleResult.Failed($"Failed to schedule message: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cancelled = await _storage.CancelAsync(scheduleId, cancellationToken);

            if (cancelled)
            {
                _logger.LogDebug("Cancelled scheduled message {ScheduleId}", scheduleId);
            }

            return cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel scheduled message {ScheduleId}", scheduleId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ScheduledMessageInfo?> GetScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await _storage.GetAsync(scheduleId, cancellationToken);
            return entry == null ? null : CreateMessageInfo(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get scheduled message {ScheduleId}", scheduleId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledMessageInfo>> GetPendingAsync(
        ScheduledMessageQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            query ??= new ScheduledMessageQuery { Status = ScheduledMessageStatus.Pending };

            var entries = await _storage.QueryAsync(query, cancellationToken);
            return entries.Select(CreateMessageInfo).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending messages");
            return Array.Empty<ScheduledMessageInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _storage.GetPendingCountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending count");
            return 0;
        }
    }

    private async Task PollForDueMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting message polling worker");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, cancellationToken);

                var dueMessages = await _storage.GetDueAsync(
                    DateTimeOffset.UtcNow,
                    _options.BatchSize,
                    cancellationToken);

                if (dueMessages.Count > 0)
                {
                    _logger.LogDebug("Found {Count} due messages", dueMessages.Count);

                    foreach (var entry in dueMessages)
                    {
                        await _deliveryChannel.Writer.WriteAsync(entry.Message, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in polling worker");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        _deliveryChannel.Writer.Complete();
        _logger.LogInformation("Message polling worker stopped");
    }

    private async Task ProcessDeliveriesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting message delivery worker");

        var tasks = new List<Task>();

        await foreach (var scheduledMessage in _deliveryChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // Limit concurrency
            if (tasks.Count >= _options.MaxConcurrency)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);
            }

            tasks.Add(DeliverMessageAsync(scheduledMessage, cancellationToken));
        }

        // Wait for remaining deliveries
        await Task.WhenAll(tasks);

        _logger.LogInformation("Message delivery worker stopped");
    }

    private async Task DeliverMessageAsync(ScheduledMessage scheduledMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _deliveryHandler.DeliverAsync(scheduledMessage, cancellationToken);
            await _storage.MarkDeliveredAsync(scheduledMessage.ScheduleId, cancellationToken);

            _logger.LogDebug(
                "Delivered scheduled message {ScheduleId}",
                scheduledMessage.ScheduleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to deliver scheduled message {ScheduleId}",
                scheduledMessage.ScheduleId);

            await _storage.MarkFailedAsync(scheduledMessage.ScheduleId, ex.Message, cancellationToken);
            await _deliveryHandler.HandleDeliveryFailureAsync(scheduledMessage.ScheduleId, ex, cancellationToken);
        }
    }

    private async Task PeriodicCleanupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting periodic cleanup worker");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, cancellationToken);

                var olderThan = DateTimeOffset.UtcNow.Subtract(_options.CleanupAge);
                var removed = await _storage.CleanupAsync(olderThan, cancellationToken);

                if (removed > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} old scheduled messages", removed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cleanup worker");
            }
        }

        _logger.LogInformation("Periodic cleanup worker stopped");
    }

    private static ScheduledMessageInfo CreateMessageInfo(ScheduledMessageEntry entry)
    {
        return new ScheduledMessageInfo
        {
            ScheduleId = entry.ScheduleId,
            MessageId = entry.Message.Message.MessageId,
            MessageType = entry.Message.Message.GetType().Name,
            DeliverAt = entry.Message.DeliverAt,
            ScheduledAt = entry.Message.ScheduledAt,
            Status = entry.Status,
            Destination = entry.Message.Options.Destination,
            Priority = entry.Message.Options.Priority,
            ErrorMessage = entry.ErrorMessage
        };
    }

    /// <summary>
    /// Asynchronously releases all resources used by the <see cref="StorageBackedScheduler"/>.
    /// Stops all background workers gracefully and waits for in-flight deliveries to complete.
    /// </summary>
    /// <returns>A task that completes when all resources have been released.</returns>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Shutting down StorageBackedScheduler");

        _shutdownCts.Cancel();

        // Wait for workers to complete
        var tasks = new List<Task> { _pollingTask, _deliveryTask };
        if (_cleanupTask != null)
        {
            tasks.Add(_cleanupTask);
        }

        await Task.WhenAll(tasks);

        _shutdownCts.Dispose();

        _logger.LogInformation("StorageBackedScheduler shutdown complete");
    }
}
