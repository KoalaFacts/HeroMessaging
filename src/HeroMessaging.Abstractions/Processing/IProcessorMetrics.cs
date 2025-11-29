using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Abstractions.Processing;

public interface IProcessorMetrics
{
    long ProcessedCount { get; }
    long FailedCount { get; }
    TimeSpan AverageDuration { get; }
}

public interface ICommandProcessor : IProcessor
{
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);
    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
    IProcessorMetrics GetMetrics();
}

public interface IQueryProcessor : IProcessor
{
    Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
    IQueryProcessorMetrics GetMetrics();
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
    DateTimeOffset? LastProcessedTime { get; }
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
    Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);
    Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default);
    Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default);
    Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default);
}

public interface IOutboxProcessor : IProcessor
{
    IOutboxProcessorMetrics GetMetrics();
    Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IInboxProcessor : IProcessor
{
    IInboxProcessorMetrics GetMetrics();
    Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default);
}