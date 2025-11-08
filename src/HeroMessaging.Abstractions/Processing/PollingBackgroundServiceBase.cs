using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Base class for background services that poll for work items and process them
/// </summary>
/// <typeparam name="TWorkItem">The type of work item to process</typeparam>
public abstract class PollingBackgroundServiceBase<TWorkItem>
{
    protected ILogger Logger { get; }
    private readonly ActionBlock<TWorkItem> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;

    protected PollingBackgroundServiceBase(
        ILogger logger,
        int maxDegreeOfParallelism = 1,
        int boundedCapacity = 100,
        bool ensureOrdered = false)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
    public Task Start(CancellationToken cancellationToken = default)
    {
        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = RunPollingLoop(_cancellationTokenSource.Token);

        Logger.LogInformation("{ServiceName} started", GetServiceName());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background polling service
    /// </summary>
    public async Task Stop()
    {
        _cancellationTokenSource?.Cancel();
        _processingBlock.Complete();

        if (_pollingTask != null)
            await _pollingTask;

        await _processingBlock.Completion;

        Logger.LogInformation("{ServiceName} stopped", GetServiceName());
    }

    /// <summary>
    /// Submits a work item for immediate processing
    /// </summary>
    protected async Task SubmitWorkItem(TWorkItem workItem, CancellationToken cancellationToken = default)
    {
        await _processingBlock.SendAsync(workItem, cancellationToken);
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
                var workItems = await PollForWorkItems(cancellationToken);

                foreach (var workItem in workItems)
                {
                    await _processingBlock.SendAsync(workItem, cancellationToken);
                }

                var hasWork = workItems.Any();
                var delay = GetPollingDelay(hasWork);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in {ServiceName} polling loop", GetServiceName());
                await Task.Delay(GetErrorDelay(), cancellationToken);
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
}
