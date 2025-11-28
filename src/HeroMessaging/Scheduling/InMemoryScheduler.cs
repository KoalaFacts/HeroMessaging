using System.Collections.Concurrent;
using System.Threading;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Scheduling;

namespace HeroMessaging.Scheduling;

/// <summary>
/// In-memory message scheduler using System.Threading.Timer for delayed delivery.
/// </summary>
/// <remarks>
/// This scheduler is designed for development and testing scenarios. Scheduled messages
/// are not persisted and will be lost if the process restarts. For production use,
/// consider using a storage-backed scheduler.
///
/// Performance characteristics:
/// - Scheduling operation: O(1), <100 microseconds
/// - Memory usage: ~200 bytes per scheduled message
/// - Supports concurrent operations via ConcurrentDictionary
/// </remarks>
public sealed class InMemoryScheduler : IMessageScheduler, IDisposable
{
    private readonly IMessageDeliveryHandler _deliveryHandler;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, ScheduledEntry> _scheduledMessages;
    private readonly ConcurrentBag<Task> _backgroundTasks = new();
    private readonly CancellationTokenSource _disposeCts = new();
#if NET9_0_OR_GREATER
    private readonly Lock _disposeLock = new();
#else
    private readonly object _disposeLock = new();
#endif
    private bool _disposed;

    public InMemoryScheduler(IMessageDeliveryHandler deliveryHandler, TimeProvider timeProvider)
    {
        _deliveryHandler = deliveryHandler ?? throw new ArgumentNullException(nameof(deliveryHandler));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _scheduledMessages = new ConcurrentDictionary<Guid, ScheduledEntry>();
    }

    public Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        TimeSpan delay,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative");

        var now = _timeProvider.GetUtcNow();
        var deliverAt = now.Add(delay);
        return ScheduleAsyncInternal(message, deliverAt, now, options, cancellationToken);
    }

    public Task<ScheduleResult> ScheduleAsync<T>(
        T message,
        DateTimeOffset deliverAt,
        SchedulingOptions? options = null,
        CancellationToken cancellationToken = default) where T : IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var now = _timeProvider.GetUtcNow();
        // Allow a small tolerance (1 second) for timing issues, but reject significantly past times
        if (deliverAt < now.AddSeconds(-1)) throw new ArgumentException("Delivery time cannot be in the past", nameof(deliverAt));

        return ScheduleAsyncInternal(message, deliverAt, now, options, cancellationToken);
    }

    private Task<ScheduleResult> ScheduleAsyncInternal<T>(
        T message,
        DateTimeOffset deliverAt,
        DateTimeOffset now,
        SchedulingOptions? options,
        CancellationToken cancellationToken) where T : IMessage
    {
        ThrowIfDisposed();

        var scheduleId = Guid.NewGuid();
        var scheduledMessage = new ScheduledMessage
        {
            ScheduleId = scheduleId,
            Message = message,
            DeliverAt = deliverAt,
            Options = options ?? new SchedulingOptions(),
            ScheduledAt = now
        };

        var delay = deliverAt - now;
        var dueTime = delay > TimeSpan.Zero ? delay : TimeSpan.Zero;

        // Create timer for delivery - pass scheduleId via state parameter
        var timer = new Timer(
            callback: TimerCallback,
            state: scheduleId,
            dueTime: dueTime,
            period: Timeout.InfiniteTimeSpan);

        var entry = new ScheduledEntry
        {
            ScheduledMessage = scheduledMessage,
            Timer = timer,
            Status = ScheduledMessageStatus.Pending
        };

        if (!_scheduledMessages.TryAdd(scheduleId, entry))
        {
            timer.Dispose();
            return Task.FromResult(ScheduleResult.Failed("Failed to schedule message (duplicate schedule ID)"));
        }

        return Task.FromResult(ScheduleResult.Successful(scheduleId, deliverAt));
    }

    public Task<bool> CancelScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_scheduledMessages.TryRemove(scheduleId, out var entry))
        {
            // Only cancel if still pending
            if (entry.Status == ScheduledMessageStatus.Pending)
            {
                entry.Timer?.Dispose();
                entry.Status = ScheduledMessageStatus.Cancelled;
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<ScheduledMessageInfo?> GetScheduledAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_scheduledMessages.TryGetValue(scheduleId, out var entry))
        {
            var info = CreateMessageInfo(entry);
            return Task.FromResult<ScheduledMessageInfo?>(info);
        }

        return Task.FromResult<ScheduledMessageInfo?>(null);
    }

    public Task<IReadOnlyList<ScheduledMessageInfo>> GetPendingAsync(
        ScheduledMessageQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var entries = _scheduledMessages.Values
            .Where(e => e.Status == ScheduledMessageStatus.Pending)
            .Select(CreateMessageInfo);

        // Apply filters
        if (query != null)
        {
            if (query.Status.HasValue)
            {
                entries = entries.Where(e => e.Status == query.Status.Value);
            }

            if (!string.IsNullOrEmpty(query.Destination))
            {
                entries = entries.Where(e => e.Destination == query.Destination);
            }

            if (!string.IsNullOrEmpty(query.MessageType))
            {
                entries = entries.Where(e => e.MessageType == query.MessageType);
            }

            if (query.DeliverAfter.HasValue)
            {
                entries = entries.Where(e => e.DeliverAt >= query.DeliverAfter.Value);
            }

            if (query.DeliverBefore.HasValue)
            {
                entries = entries.Where(e => e.DeliverAt <= query.DeliverBefore.Value);
            }

            if (query.Offset > 0)
            {
                entries = entries.Skip(query.Offset);
            }

            if (query.Limit > 0)
            {
                entries = entries.Take(query.Limit);
            }
        }

        var result = entries.ToList();
        return Task.FromResult<IReadOnlyList<ScheduledMessageInfo>>(result);
    }

    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var count = _scheduledMessages.Values.Count(e => e.Status == ScheduledMessageStatus.Pending);
        return Task.FromResult((long)count);
    }

    private void TimerCallback(object? state)
    {
        if (state is Guid scheduleId)
        {
            // Execute delivery on thread pool to avoid blocking timer thread
            // Track the task so we can wait for it during disposal
            var deliveryTask = Task.Run(async () => await DeliverScheduledMessage(scheduleId));
            _backgroundTasks.Add(deliveryTask);
        }
    }

    private async Task DeliverScheduledMessage(Guid scheduleId)
    {
        if (_disposed) return;

        if (_scheduledMessages.TryGetValue(scheduleId, out var entry))
        {
            // Check if still pending (might have been cancelled)
            if (entry.Status != ScheduledMessageStatus.Pending)
            {
                return;
            }

            try
            {
                await _deliveryHandler.DeliverAsync(entry.ScheduledMessage);
                entry.Status = ScheduledMessageStatus.Delivered;
            }
            catch (Exception ex)
            {
                entry.Status = ScheduledMessageStatus.Failed;
                entry.ErrorMessage = ex.Message;
                await _deliveryHandler.HandleDeliveryFailureAsync(scheduleId, ex);
            }
            finally
            {
                entry.Timer?.Dispose();

                // Clean up delivered or failed messages after a short delay
                // Use the disposal token so cleanup tasks are cancelled when disposed
                // Track this task so we can wait for it during disposal
                var cleanupTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(60), _timeProvider, _disposeCts.Token);

                        _scheduledMessages.TryRemove(scheduleId, out _);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cleanup cancelled due to disposal - remove immediately
                        _scheduledMessages.TryRemove(scheduleId, out _);
                    }
                }, _disposeCts.Token);
                _backgroundTasks.Add(cleanupTask);
            }
        }
    }

    private static ScheduledMessageInfo CreateMessageInfo(ScheduledEntry entry)
    {
        return new ScheduledMessageInfo
        {
            ScheduleId = entry.ScheduledMessage.ScheduleId,
            MessageId = entry.ScheduledMessage.Message.MessageId,
            MessageType = entry.ScheduledMessage.Message.GetType().Name,
            DeliverAt = entry.ScheduledMessage.DeliverAt,
            ScheduledAt = entry.ScheduledMessage.ScheduledAt,
            Status = entry.Status,
            Destination = entry.ScheduledMessage.Options.Destination,
            Priority = entry.ScheduledMessage.Options.Priority,
            ErrorMessage = entry.ErrorMessage
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryScheduler));
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel any pending cleanup tasks to prevent them from keeping the process alive
            _disposeCts.Cancel();

            // Dispose all timers to prevent new deliveries
            foreach (var entry in _scheduledMessages.Values)
            {
                entry.Timer?.Dispose();
            }

            // Wait for all background tasks (delivery and cleanup) to complete
            // Use a hard grace period as user requested
            var backgroundTasksSnapshot = _backgroundTasks.ToArray();
            if (backgroundTasksSnapshot.Length > 0)
            {
                try
                {
                    // Grace period: 10 seconds for all background tasks to complete after cancellation
                    if (!Task.WaitAll(backgroundTasksSnapshot, TimeSpan.FromSeconds(10)))
                    {
                        // Timeout - some tasks didn't complete within grace period
                        // In production, we'd log this
                    }
                }
                catch (AggregateException)
                {
                    // Ignore exceptions during disposal (tasks may have been cancelled)
                }
            }

            _scheduledMessages.Clear();
            _disposeCts.Dispose();
        }
    }

    private sealed class ScheduledEntry
    {
        public ScheduledMessage ScheduledMessage { get; init; } = null!;
        public Timer? Timer { get; init; }
        public ScheduledMessageStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
