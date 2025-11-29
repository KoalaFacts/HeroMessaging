using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Base class for background services that poll for work items and process them
/// </summary>
/// <typeparam name="TWorkItem">The type of work item to process</typeparam>
public abstract class PollingBackgroundServiceBase<TWorkItem> : IAsyncDisposable
{
    protected ILogger Logger { get; }
    protected TimeProvider TimeProvider { get; }
    private readonly ActionBlock<TWorkItem> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    private bool _disposed;

    protected PollingBackgroundServiceBase(
        ILogger logger,
        TimeProvider timeProvider,
        int maxDegreeOfParallelism = 1,
        int boundedCapacity = 100,
        bool ensureOrdered = false)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _processingBlock = new ActionBlock<TWorkItem>(
            ProcessWorkItem,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                BoundedCapacity = boundedCapacity,
                EnsureOrdered = ensureOrdered
            });
    }

    /// <summary>
    /// Starts the background polling service
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = RunPollingLoop(_cancellationTokenSource.Token);

        Logger.LogInformation("{ServiceName} started", GetServiceName());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets a value indicating whether the service is currently running
    /// </summary>
    public bool IsRunning => _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested;

    /// <summary>
    /// Stops the background polling service
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource == null)
            return;

        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
        _processingBlock.Complete();

        if (_pollingTask != null)
            await _pollingTask.ConfigureAwait(false);

        await _processingBlock.Completion.ConfigureAwait(false);

        Logger.LogInformation("{ServiceName} stopped", GetServiceName());
    }

    /// <summary>
    /// Submits a work item for immediate processing
    /// </summary>
    protected async Task SubmitWorkItem(TWorkItem workItem, CancellationToken cancellationToken = default)
    {
        await _processingBlock.SendAsync(workItem, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Main polling loop that retrieves work items and submits them for processing
    /// </summary>
    private async Task RunPollingLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var workItems = await PollForWorkItems(cancellationToken).ConfigureAwait(false);

                foreach (var workItem in workItems)
                {
                    await _processingBlock.SendAsync(workItem, cancellationToken).ConfigureAwait(false);
                }

                var hasWork = workItems.Any();
                var delay = GetPollingDelay(hasWork);

                await Task.Delay(delay, TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in {ServiceName} polling loop", GetServiceName());
                await Task.Delay(GetErrorDelay(), TimeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Override to provide the service name for logging
    /// </summary>
    protected abstract string GetServiceName();

    /// <summary>
    /// Override to poll for work items from the data source
    /// </summary>
    protected abstract Task<IEnumerable<TWorkItem>> PollForWorkItems(CancellationToken cancellationToken);

    /// <summary>
    /// Override to process a single work item
    /// </summary>
    protected abstract Task ProcessWorkItem(TWorkItem workItem);

    /// <summary>
    /// Override to customize polling delay based on whether work was found
    /// Default: 100ms if work found, 1000ms if no work
    /// </summary>
    protected virtual TimeSpan GetPollingDelay(bool hasWork)
    {
        return hasWork ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(1000);
    }

    /// <summary>
    /// Override to customize error delay
    /// Default: 5 seconds
    /// </summary>
    protected virtual TimeSpan GetErrorDelay()
    {
        return TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Disposes the background service and releases resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
