using System.Collections.Concurrent;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
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

    public async Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!await _queueStorage.QueueExistsAsync(queueName, cancellationToken))
        {
            await _queueStorage.CreateQueueAsync(queueName, null, cancellationToken);
        }

        await _queueStorage.EnqueueAsync(queueName, message, options, cancellationToken);
        _logger.LogDebug("Message enqueued to {QueueName} with priority {Priority}", queueName, options?.Priority ?? 0);
    }

    public async Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default)
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

        await worker.StartAsync(cancellationToken);
    }

    public async Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_workers.TryRemove(queueName, out var worker))
        {
            await worker.StopAsync();
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
}