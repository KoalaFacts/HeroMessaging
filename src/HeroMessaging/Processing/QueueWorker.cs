using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Worker that processes messages from a specific queue.
/// Handles polling, message processing, and acknowledgment/rejection.
/// </summary>
internal sealed class QueueWorker
{
    /// <summary>Maximum number of messages that can be queued for processing.</summary>
    private const int DefaultBoundedCapacity = 100;

    /// <summary>Delay between polls when queue is empty (milliseconds).</summary>
    private const int EmptyQueuePollDelayMs = 100;

    /// <summary>Delay after an error before retrying (milliseconds).</summary>
    private const int ErrorRecoveryDelayMs = 1000;

    /// <summary>Maximum number of times to requeue a failed message before sending to DLQ.</summary>
    private const int MaxRequeueAttempts = 3;

    private readonly string _queueName;
    private readonly IQueueStorage _storage;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly ActionBlock<QueueEntry> _processingBlock;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueWorker"/> class.
    /// </summary>
    /// <param name="queueName">The name of the queue to process.</param>
    /// <param name="storage">The queue storage to poll from.</param>
    /// <param name="serviceProvider">The service provider for resolving handlers.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public QueueWorker(
        string queueName,
        IQueueStorage storage,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _processingBlock = new ActionBlock<QueueEntry>(
            ProcessMessage,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                BoundedCapacity = DefaultBoundedCapacity
            });
    }

    /// <summary>
    /// Starts processing the queue.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task Start(CancellationToken cancellationToken)
    {
        if (_cancellationTokenSource != null)
            return Task.CompletedTask;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = PollQueue(_cancellationTokenSource.Token);

        _logger.LogInformation("Queue worker started for {QueueName}", _queueName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops processing the queue.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

        // Dispose CancellationTokenSource to prevent memory leak
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _logger.LogInformation("Queue worker stopped for {QueueName}", _queueName);
    }

    private async Task PollQueue(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var entry = await _storage.DequeueAsync(_queueName, cancellationToken);
                if (entry != null)
                {
                    await _processingBlock.SendAsync(entry, cancellationToken);
                }
                else
                {
                    await Task.Delay(EmptyQueuePollDelayMs, cancellationToken);
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
                    await Task.Delay(ErrorRecoveryDelayMs, cancellationToken);
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

            await MessageDispatcher.DispatchAsync(messaging, entry.Message, _logger, $"queue:{_queueName}");

            await _storage.AcknowledgeAsync(_queueName, entry.Id);
            _logger.LogDebug("Message {MessageId} processed successfully from queue {QueueName}",
                entry.Message.MessageId, _queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId} from queue {QueueName}",
                entry.Message.MessageId, _queueName);

            await _storage.RejectAsync(_queueName, entry.Id, requeue: entry.DequeueCount < MaxRequeueAttempts);
        }
    }
}
