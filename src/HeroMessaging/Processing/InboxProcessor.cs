using System.Collections.Concurrent;
using System.Data.Common;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;
/// <summary>
/// Represents the inbox processor type.
/// </summary>

public class InboxProcessor : PollingBackgroundServiceBase<InboxEntry>, IInboxProcessor, IAsyncDisposable
{
    private readonly IInboxStorage _inboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _stopTimedOut;
    /// <summary>
    /// Represents cleanup task.
    /// </summary>
    private Task? _cleanupTask;
    private CancellationTokenSource? _cleanupCancellationTokenSource;
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxProcessor"/> class.
    /// </summary>

    public InboxProcessor(
        IInboxStorage inboxStorage,
        IServiceProvider serviceProvider,
        ILogger<InboxProcessor> logger,
        TimeProvider timeProvider)
        : base(logger, timeProvider, maxDegreeOfParallelism: 1, boundedCapacity: 100, ensureOrdered: true)
    {
        _inboxStorage = inboxStorage;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Executes process incoming async.
    /// </summary>

    public async Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new InboxOptions();

        // Check for duplicates if idempotency is required
        if (options.RequireIdempotency)
        {
            var isDuplicate = await _inboxStorage.IsDuplicateAsync(
                message.MessageId.ToString(),
                options.DeduplicationWindow,
                cancellationToken);

            if (isDuplicate)
            {
                Logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", message.MessageId);
                return false;
            }
        }

        // Add to inbox
        var entry = await _inboxStorage.AddAsync(message, options, cancellationToken);

        if (entry == null)
        {
            Logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }

        if (_inFlight.TryAdd(entry.Id, 0))
        {
            try
            {
                await SubmitWorkItemAsync(entry, cancellationToken);
            }
            catch
            {
                _inFlight.TryRemove(entry.Id, out _);
                throw;
            }
        }

        return true;
    }
    /// <summary>
    /// Executes start async.
    /// </summary>

    public new Task StartAsync(CancellationToken cancellationToken = default)
    {
        var start = base.StartAsync(cancellationToken);
        if (_cleanupCancellationTokenSource is null)
        {
            _cleanupCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            _cleanupTask = RunCleanup(_cleanupCancellationTokenSource.Token);
        }

        return start;
    }
    /// <summary>
    /// Executes stop async.
    /// </summary>

    public new async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cleanupCancellationTokenSource?.Cancel();

        try
        {
            await base.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_cleanupTask is not null)
                await _cleanupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _shutdown.CancelAsync().ConfigureAwait(false);
            _stopTimedOut = true;
            Logger.LogWarning("Inbox processor did not drain before the host shutdown deadline");
        }
    }

    Task IInboxProcessor.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

    /// <summary>
    /// Disposes the processor without waiting again after a host shutdown deadline has expired.
    /// </summary>
    public new async ValueTask DisposeAsync()
    {
        if (_stopTimedOut)
            return;

        await StopAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
        _cleanupCancellationTokenSource?.Dispose();
        _shutdown.Dispose();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();
    /// <summary>
    /// Gets is running.
    /// </summary>

    public new bool IsRunning => base.IsRunning;
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IInboxProcessorMetrics GetMetrics()
    {
        return new InboxProcessorMetrics
        {
            ProcessedMessages = 0, // TODO: Track metrics
            DuplicateMessages = 0,
            FailedMessages = 0,
            DeduplicationRate = 0.0
        };
    }
    /// <summary>
    /// Represents the inbox processor metrics type.
    /// </summary>

    private class InboxProcessorMetrics : IInboxProcessorMetrics
    {
        public long ProcessedMessages { get; init; }
        public long DuplicateMessages { get; init; }
        public long FailedMessages { get; init; }
        /// <summary>
        /// Gets deduplication rate.
        /// </summary>
        public double DeduplicationRate { get; init; }
    }
    /// <summary>
    /// Executes get service name.
    /// </summary>

    protected override string GetServiceName() => "Inbox processor";
    /// <summary>
    /// Executes poll for work items async.
    /// </summary>

    protected override async Task<IEnumerable<InboxEntry>> PollForWorkItemsAsync(CancellationToken cancellationToken)
    {
        var pending = await _inboxStorage.GetUnprocessedAsync(100, cancellationToken);
        return [.. pending.Where(entry => _inFlight.TryAdd(entry.Id, 0))];
    }
    /// <summary>
    /// Executes get polling delay.
    /// </summary>

    protected override TimeSpan GetPollingDelay(bool hasWork)
    {
        return hasWork ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5);
    }
    /// <summary>
    /// Executes run cleanup.
    /// </summary>

    private async Task RunCleanup(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), _timeProvider, cancellationToken);

                await _inboxStorage.CleanupOldEntriesAsync(TimeSpan.FromDays(7), cancellationToken);

                Logger.LogDebug("Inbox cleanup completed");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during inbox cleanup");
            }
        }
    }
    /// <summary>
    /// Executes process work item async.
    /// </summary>

    protected override async Task ProcessWorkItemAsync(InboxEntry entry)
    {
        IAsyncDisposable? claim = null;
        var dispatchStarted = false;
        try
        {
            if (_inboxStorage is IInboxClaimStorage claimStorage)
            {
                claim = await claimStorage.TryClaimAsync(entry.Id, _shutdown.Token);
                if (claim is null)
                    return;

                var persisted = await _inboxStorage.GetAsync(entry.Id, _shutdown.Token);
                if (persisted?.Status != InboxStatus.Pending)
                    return;
                entry = persisted;
            }

            entry.Status = InboxStatus.Processing;

            dispatchStarted = true;
            await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "inbox", _shutdown.Token);

            await _inboxStorage.MarkProcessedAsync(entry.Id);

            Logger.LogInformation("Inbox entry {EntryId} (Message: {MessageId}) processed successfully from source {Source}",
                entry.Id, entry.Message.MessageId, entry.Options.Source ?? "Unknown");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            entry.Status = InboxStatus.Pending;
            Logger.LogInformation("Inbox entry {EntryId} will be retried after shutdown", entry.Id);
        }
        catch (Exception ex) when (_shutdown.IsCancellationRequested)
        {
            entry.Status = InboxStatus.Pending;
            Logger.LogWarning(ex, "Inbox entry {EntryId} failed during shutdown and will be retried", entry.Id);
        }
        catch (Exception ex) when (!dispatchStarted)
        {
            Logger.LogError(ex, "Unable to claim inbox entry {EntryId}; it remains pending", entry.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing inbox entry {EntryId} (Message: {MessageId})",
                entry.Id, entry.Message.MessageId);

            await _inboxStorage.MarkFailedAsync(entry.Id, ex.Message);
        }
        finally
        {
            try
            {
                if (claim is not null)
                    await claim.DisposeAsync();
            }
            catch (DbException ex)
            {
                Logger.LogError(ex, "Unable to release inbox claim for {EntryId}", entry.Id);
            }
            finally
            {
                _inFlight.TryRemove(entry.Id, out _);
            }
        }
    }
    /// <summary>
    /// Executes get unprocessed count async.
    /// </summary>

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCountAsync(cancellationToken);
    }
}
