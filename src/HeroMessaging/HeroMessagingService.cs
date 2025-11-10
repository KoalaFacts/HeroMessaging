using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;

namespace HeroMessaging;

public class HeroMessagingService(
    ICommandProcessor commandProcessor,
    IQueryProcessor queryProcessor,
    IEventBus eventBus,
    TimeProvider timeProvider,
    IQueueProcessor? queueProcessor = null,
    IOutboxProcessor? outboxProcessor = null,
    IInboxProcessor? inboxProcessor = null) : IHeroMessaging
{
    private readonly ICommandProcessor _commandProcessor = commandProcessor;
    private readonly IQueryProcessor _queryProcessor = queryProcessor;
    private readonly IEventBus _eventBus = eventBus;
    private readonly IQueueProcessor? _queueProcessor = queueProcessor;
    private readonly IOutboxProcessor? _outboxProcessor = outboxProcessor;
    private readonly IInboxProcessor? _inboxProcessor = inboxProcessor;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly MessagingMetrics _metrics = new();

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        _metrics.CommandsSent++;
        await _commandProcessor.Send(command, cancellationToken);
    }

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        _metrics.CommandsSent++;
        return await _commandProcessor.Send(command, cancellationToken);
    }

    public async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        _metrics.QueriesSent++;
        return await _queryProcessor.Send(query, cancellationToken);
    }

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        _metrics.EventsPublished++;
        await _eventBus.Publish(@event, cancellationToken);
    }

    public async Task<IReadOnlyList<bool>> SendBatchAsync(IReadOnlyList<ICommand> commands, CancellationToken cancellationToken = default)
    {
        if (commands == null || commands.Count == 0)
            return Array.Empty<bool>();

        var results = new List<bool>(commands.Count);
        _metrics.CommandsSent += commands.Count;

        foreach (var command in commands)
        {
            try
            {
                await _commandProcessor.Send(command, cancellationToken);
                results.Add(true);
            }
            catch
            {
                results.Add(false);
                throw; // Re-throw to maintain error handling behavior
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TResponse>> SendBatchAsync<TResponse>(IReadOnlyList<ICommand<TResponse>> commands, CancellationToken cancellationToken = default)
    {
        if (commands == null || commands.Count == 0)
            return Array.Empty<TResponse>();

        var results = new List<TResponse>(commands.Count);
        _metrics.CommandsSent += commands.Count;

        foreach (var command in commands)
        {
            var result = await _commandProcessor.Send(command, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    public async Task<IReadOnlyList<bool>> PublishBatchAsync(IReadOnlyList<IEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0)
            return Array.Empty<bool>();

        var results = new List<bool>(events.Count);
        _metrics.EventsPublished += events.Count;

        foreach (var @event in events)
        {
            try
            {
                await _eventBus.Publish(@event, cancellationToken);
                results.Add(true);
            }
            catch
            {
                results.Add(false);
                throw; // Re-throw to maintain error handling behavior
            }
        }

        return results;
    }

    public async Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        _metrics.MessagesQueued++;
        await _queueProcessor.Enqueue(message, queueName, options, cancellationToken);
    }

    public async Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        await _queueProcessor.StartQueue(queueName, cancellationToken);
    }

    public async Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        await _queueProcessor.StopQueue(queueName, cancellationToken);
    }

    public async Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_outboxProcessor == null)
            throw new InvalidOperationException("Outbox functionality is not enabled. Call WithOutbox() during configuration.");

        _metrics.OutboxMessages++;
        await _outboxProcessor.PublishToOutbox(message, options, cancellationToken);
    }

    public async Task ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_inboxProcessor == null)
            throw new InvalidOperationException("Inbox functionality is not enabled. Call WithInbox() during configuration.");

        _metrics.InboxMessages++;
        await _inboxProcessor.ProcessIncoming(message, options, cancellationToken);
    }

    public MessagingMetrics GetMetrics()
    {
        return _metrics;
    }

    public MessagingHealth GetHealth()
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        return new MessagingHealth
        {
            IsHealthy = true,
            Components = new Dictionary<string, ComponentHealth>
            {
                ["CommandProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["QueryProcessor"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["EventBus"] = new ComponentHealth { IsHealthy = true, Status = "Operational", LastChecked = now },
                ["QueueProcessor"] = new ComponentHealth { IsHealthy = _queueProcessor != null, Status = _queueProcessor != null ? "Operational" : "Not Configured", LastChecked = now },
                ["OutboxProcessor"] = new ComponentHealth { IsHealthy = _outboxProcessor != null, Status = _outboxProcessor != null ? "Operational" : "Not Configured", LastChecked = now },
                ["InboxProcessor"] = new ComponentHealth { IsHealthy = _inboxProcessor != null, Status = _inboxProcessor != null ? "Operational" : "Not Configured", LastChecked = now }
            }
        };
    }
}