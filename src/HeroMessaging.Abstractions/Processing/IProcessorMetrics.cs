using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Abstractions.Processing;

public interface IProcessorMetrics
{
    long ProcessedCount { get; }
    long FailedCount { get; }
    TimeSpan AverageDuration { get; }
}

public interface IEventBusMetrics
{
    long PublishedCount { get; }
    long FailedCount { get; }
    int RegisteredHandlers { get; }
}

public interface IQueryProcessorMetrics : IProcessorMetrics
{
    double CacheHitRate { get; }
}

public interface IQueueProcessorMetrics
{
    long TotalMessages { get; }
    long ProcessedMessages { get; }
    long FailedMessages { get; }
}

public interface IOutboxProcessorMetrics
{
    long PendingMessages { get; }
    long ProcessedMessages { get; }
    long FailedMessages { get; }
    DateTime? LastProcessedTime { get; }
}

public interface IInboxProcessorMetrics
{
    long ProcessedMessages { get; }
    long DuplicateMessages { get; }
    long FailedMessages { get; }
    double DeduplicationRate { get; }
}

public interface IProcessor
{
    bool IsRunning { get; }
}

public interface IQueueProcessor : IProcessor
{
    IQueueProcessorMetrics GetMetrics();
    Task<IEnumerable<string>> GetActiveQueuesAsync(CancellationToken cancellationToken = default);
    Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);
    Task StartQueue(string queueName, CancellationToken cancellationToken = default);
    Task StopQueue(string queueName, CancellationToken cancellationToken = default);
    Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default);
}

public interface IOutboxProcessor : IProcessor
{
    IOutboxProcessorMetrics GetMetrics();
    Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IInboxProcessor : IProcessor
{
    IInboxProcessorMetrics GetMetrics();
    Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default);
}