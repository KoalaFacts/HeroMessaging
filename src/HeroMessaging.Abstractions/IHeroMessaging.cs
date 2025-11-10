using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions;

public interface IHeroMessaging
{
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);

    // Batch operations
    Task<IReadOnlyList<bool>> SendBatchAsync(IReadOnlyList<ICommand> commands, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TResponse>> SendBatchAsync<TResponse>(IReadOnlyList<ICommand<TResponse>> commands, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<bool>> PublishBatchAsync(IReadOnlyList<IEvent> events, CancellationToken cancellationToken = default);

    Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default);

    Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default);

    Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);

    Task ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);

    MessagingMetrics GetMetrics();

    MessagingHealth GetHealth();
}

public record EnqueueOptions
{
    public int Priority { get; init; } = 0;
    public TimeSpan? Delay { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record OutboxOptions
{
    public string? Destination { get; init; }
    public int Priority { get; init; } = 0;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan? RetryDelay { get; init; }
}

public record InboxOptions
{
    public string? Source { get; init; }
    public bool RequireIdempotency { get; init; } = true;
    public TimeSpan? DeduplicationWindow { get; init; }
}

public class MessagingMetrics
{
    public long CommandsSent { get; set; }
    public long QueriesSent { get; set; }
    public long EventsPublished { get; set; }
    public long MessagesQueued { get; set; }
    public long OutboxMessages { get; set; }
    public long InboxMessages { get; set; }
    public Dictionary<string, long> QueueDepths { get; set; } = new();
    public Dictionary<string, double> AverageProcessingTime { get; set; } = new();
}

public class MessagingHealth
{
    public bool IsHealthy { get; set; }
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}

public class ComponentHealth
{
    public bool IsHealthy { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public DateTime LastChecked { get; set; }
}