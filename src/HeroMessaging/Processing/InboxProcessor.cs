using System.Collections.Concurrent;
using System.Data.Common;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Idempotency;
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
    private readonly ConcurrentDictionary<string, TransientFailureState> _transientFailures = new();
    private static readonly DefaultIdempotencyPolicy FailureClassifier = new();

    /// <summary>
    /// Handler attempts per entry and process before a transient failure becomes terminal.
    /// </summary>
    internal const int MaxHandlerAttempts = 3;

    /// <summary>
    /// Delay before the second attempt; doubles for each later attempt.
    /// </summary>
    internal static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _stopTimedOut;
    private long _acceptedMessages;
    private long _processedMessages;
    private long _duplicateMessages;
    private long _failedMessages;
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
                Interlocked.Increment(ref _duplicateMessages);
                Logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", message.MessageId);
                return false;
            }
        }

        // Add to inbox
        var entry = await _inboxStorage.AddAsync(message, options, cancellationToken);

        if (entry == null)
        {
            Interlocked.Increment(ref _duplicateMessages);
            Logger.LogWarning("Message {MessageId} was rejected as duplicate", message.MessageId);
            return false;
        }

        Interlocked.Increment(ref _acceptedMessages);

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
    /// Gets the counts this processor instance has observed since it was created (they are not persisted and are not shared between hosts).
    /// Duplicates include messages rejected by the pre-insert check and by storage on insert; the deduplication rate is duplicates divided by
    /// all incoming messages (duplicates plus accepted). Failed counts dispatch or completion failures after dispatch started (the entry is then marked failed).
    /// </summary>

    public IInboxProcessorMetrics GetMetrics()
    {
        var duplicates = Interlocked.Read(ref _duplicateMessages);
        var incoming = duplicates + Interlocked.Read(ref _acceptedMessages);
        return new InboxProcessorMetrics
        {
            ProcessedMessages = Interlocked.Read(ref _processedMessages),
            DuplicateMessages = duplicates,
            FailedMessages = Interlocked.Read(ref _failedMessages),
            DeduplicationRate = incoming == 0 ? 0.0 : (double)duplicates / incoming
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
        var now = _timeProvider.GetUtcNow();
        return [.. pending.Where(entry => IsDue(entry.Id, now) && _inFlight.TryAdd(entry.Id, 0))];
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
                PruneStaleTransientFailures(_timeProvider.GetUtcNow() - TimeSpan.FromHours(1));

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
                {
                    _transientFailures.TryRemove(entry.Id, out _);
                    return;
                }
                entry = persisted;
            }

            entry.Status = InboxStatus.Processing;

            dispatchStarted = true;
            await ScopedMessagingExecutor.DispatchAsync(_serviceProvider, entry.Message, Logger, "inbox", _shutdown.Token);

            await _inboxStorage.MarkProcessedAsync(entry.Id);
            Interlocked.Increment(ref _processedMessages);
            _transientFailures.TryRemove(entry.Id, out _);

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
        catch (Exception ex) when (TryScheduleRetry(entry, ex))
        {
            entry.Status = InboxStatus.Pending;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedMessages);
            _transientFailures.TryRemove(entry.Id, out _);
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
    /// Drops retry state for entries this worker has not retried for a long time
    /// (processed by another host, deleted, or abandoned at shutdown).
    /// </summary>
    internal void PruneStaleTransientFailures(DateTimeOffset olderThan)
    {
        foreach (var pair in _transientFailures.Where(pair => pair.Value.NotBefore < olderThan))
            _transientFailures.TryRemove(pair.Key, out _);
    }

    internal int TransientFailureCount => _transientFailures.Count;

    private bool IsDue(string entryId, DateTimeOffset now) =>
        !_transientFailures.TryGetValue(entryId, out var state) || state.NotBefore <= now;

    /// <summary>
    /// Leaves an entry pending for another attempt when the failure is transient
    /// (not classified as permanent by <see cref="DefaultIdempotencyPolicy"/>) and attempts remain.
    /// An event handler failure wrapped by the dispatcher (<c>[HM-DISPATCH-EVENT-HANDLER]</c>) is classified
    /// by the handler's own exception; any other exception is classified as thrown, so a permanent
    /// exception that wraps a transient cause still fails at once.
    /// </summary>
    private bool TryScheduleRetry(InboxEntry entry, Exception exception)
    {
        if (FailureClassifier.IsIdempotentFailure(UnwrapDispatcherFailure(exception)))
            return false;

        var attempts = _transientFailures.TryGetValue(entry.Id, out var previous) ? previous.Attempts + 1 : 1;
        if (attempts >= MaxHandlerAttempts)
            return false;

        var delay = TimeSpan.FromTicks(InitialRetryDelay.Ticks << (attempts - 1));
        _transientFailures[entry.Id] = new TransientFailureState(attempts, _timeProvider.GetUtcNow() + delay);
        Logger.LogWarning(exception,
            "Transient failure processing inbox entry {EntryId} (attempt {Attempt} of {MaxAttempts}); retrying after {Delay}",
            entry.Id, attempts, MaxHandlerAttempts, delay);
        return true;
    }

    private static Exception UnwrapDispatcherFailure(Exception exception) =>
        exception is InvalidOperationException { InnerException: { } inner } wrapper
            && wrapper.Message.StartsWith(MessageDispatcher.EventHandlerFailurePrefix, StringComparison.Ordinal)
            ? inner
            : exception;

    private readonly record struct TransientFailureState(int Attempts, DateTimeOffset NotBefore);

    /// <summary>
    /// Executes get unprocessed count async.
    /// </summary>

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _inboxStorage.GetUnprocessedCountAsync(cancellationToken);
    }
}
