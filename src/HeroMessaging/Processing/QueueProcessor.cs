using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class QueueProcessor(
    IServiceProvider serviceProvider,
    IQueueStorage queueStorage,
    ILogger<QueueProcessor> logger) : IQueueProcessor
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IQueueStorage _queueStorage = queueStorage;
    private readonly ILogger<QueueProcessor> _logger = logger;
    private readonly ConcurrentDictionary<string, QueueWorker> _workers = new();

    public async Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!await _queueStorage.QueueExistsAsync(queueName, cancellationToken))
        {
            await _queueStorage.CreateQueueAsync(queueName, null, cancellationToken);
        }

        await _queueStorage.EnqueueAsync(queueName, message, options, cancellationToken);
        _logger.LogDebug("Message enqueued to {QueueName} with priority {Priority}", queueName, options?.Priority ?? 0);
    }

    public async Task StartQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (!await _queueStorage.QueueExistsAsync(queueName, cancellationToken))
        {
            await _queueStorage.CreateQueueAsync(queueName, null, cancellationToken);
        }

        var worker = _workers.GetOrAdd(queueName, _ => new QueueWorker(
            queueName,
            _queueStorage,
            _serviceProvider,
            _logger));

        await worker.Start(cancellationToken);
    }

    public async Task StopQueue(string queueName, CancellationToken cancellationToken = default)
    {
        if (_workers.TryRemove(queueName, out var worker))
        {
            await worker.Stop();
        }
    }

    public async Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _queueStorage.GetQueueDepthAsync(queueName, cancellationToken);
    }

    public bool IsRunning => _workers.Any(w => w.Value != null);

    public IQueueProcessorMetrics GetMetrics()
    {
        return new QueueProcessorMetrics
        {
            TotalMessages = 0, // TODO: Track metrics
            ProcessedMessages = 0,
            FailedMessages = 0
        };
    }

    public Task<IEnumerable<string>> GetActiveQueuesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<string>>(_workers.Keys.ToList());
    }

    private class QueueProcessorMetrics : IQueueProcessorMetrics
    {
        public long TotalMessages { get; init; }
        public long ProcessedMessages { get; init; }
        public long FailedMessages { get; init; }
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
                    var entry = await _storage.DequeueAsync(_queueName, cancellationToken);
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
                        await messaging.SendAsync(command);
                        break;
                    case IEvent @event:
                        await messaging.PublishAsync(@event);
                        break;
                    default:
                        _logger.LogWarning("Unknown message type in queue: {MessageType}", entry.Message.GetType().Name);
                        break;
                }

                await _storage.AcknowledgeAsync(_queueName, entry.Id);
                _logger.LogDebug("Message {MessageId} processed successfully from queue {QueueName}",
                    entry.Message.MessageId, _queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId} from queue {QueueName}",
                    entry.Message.MessageId, _queueName);

                await _storage.RejectAsync(_queueName, entry.Id, requeue: entry.DequeueCount < 3);
            }
        }
    }
}