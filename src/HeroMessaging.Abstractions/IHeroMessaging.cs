using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;

namespace HeroMessaging.Abstractions;

public interface IHeroMessaging
{
    Task Send(ICommand command, CancellationToken cancellationToken = default);
    
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
    
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
    
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
    
    Task Enqueue(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);
    
    Task StartQueue(string queueName, CancellationToken cancellationToken = default);
    
    Task StopQueue(string queueName, CancellationToken cancellationToken = default);
    
    Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);
    
    Task ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);
    
    MessagingMetrics GetMetrics();
    
    MessagingHealth GetHealth();
}

public class EnqueueOptions
{
    public int Priority { get; set; } = 0;
    public TimeSpan? Delay { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class OutboxOptions
{
    public string? Destination { get; set; }
    public int Priority { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan? RetryDelay { get; set; }
}

public class InboxOptions
{
    public string? Source { get; set; }
    public bool RequireIdempotency { get; set; } = true;
    public TimeSpan? DeduplicationWindow { get; set; }
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