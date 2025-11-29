using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging;

/// <summary>
/// Default implementation of <see cref="IHeroMessaging"/> providing unified access to
/// all messaging operations in the HeroMessaging framework.
/// </summary>
/// <remarks>
/// <para>
/// HeroMessagingService acts as a facade over the command processor, query processor,
/// event bus, and optional queue/outbox/inbox processors. It provides a single entry point
/// for all messaging operations and tracks metrics across all operations.
/// </para>
/// <para>
/// This service is typically registered as a singleton via <c>services.AddHeroMessaging()</c>
/// and injected where needed.
/// </para>
/// </remarks>
/// <param name="commandProcessor">The processor for handling commands.</param>
/// <param name="queryProcessor">The processor for handling queries.</param>
/// <param name="eventBus">The event bus for publishing events.</param>
/// <param name="timeProvider">The time provider for timestamp operations.</param>
/// <param name="logger">Optional logger for diagnostic output.</param>
/// <param name="queueProcessor">Optional processor for queue operations.</param>
/// <param name="outboxProcessor">Optional processor for outbox operations.</param>
/// <param name="inboxProcessor">Optional processor for inbox operations.</param>
public class HeroMessagingService(
    ICommandProcessor commandProcessor,
    IQueryProcessor queryProcessor,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<HeroMessagingService>? logger = null,
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
    private readonly ILogger<HeroMessagingService>? _logger = logger;

    // Thread-safe counters for metrics
    private long _commandsSent;
    private long _queriesSent;
    private long _eventsPublished;
    private long _messagesQueued;
    private long _outboxMessages;
    private long _inboxMessages;

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commandsSent);
        await _commandProcessor.Send(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commandsSent);
        return await _commandProcessor.Send(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _queriesSent);
        return await _queryProcessor.Send(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _eventsPublished);
        await _eventBus.Publish(@event, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<bool>> SendBatchAsync(IReadOnlyList<ICommand> commands, CancellationToken cancellationToken = default)
    {
        if (commands == null || commands.Count == 0)
            return Array.Empty<bool>();

        var results = new List<bool>(commands.Count);
        Interlocked.Add(ref _commandsSent, commands.Count);

        foreach (var command in commands)
        {
            try
            {
                await _commandProcessor.Send(command, cancellationToken).ConfigureAwait(false);
                results.Add(true);
            }
            catch (OperationCanceledException)
            {
                // Cancellation should stop batch processing immediately
                throw;
            }
            catch (Exception ex)
            {
                // Track failure but continue processing remaining commands
                _logger?.LogWarning(ex, "Failed to process command {CommandType} in batch", command.GetType().Name);
                results.Add(false);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TResponse>> SendBatchAsync<TResponse>(IReadOnlyList<ICommand<TResponse>> commands, CancellationToken cancellationToken = default)
    {
        if (commands == null || commands.Count == 0)
            return Array.Empty<TResponse>();

        var results = new List<TResponse>(commands.Count);
        Interlocked.Add(ref _commandsSent, commands.Count);

        foreach (var command in commands)
        {
            var result = await _commandProcessor.Send(command, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }

    public async Task<IReadOnlyList<bool>> PublishBatchAsync(IReadOnlyList<IEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0)
            return Array.Empty<bool>();

        var results = new List<bool>(events.Count);
        Interlocked.Add(ref _eventsPublished, events.Count);

        foreach (var @event in events)
        {
            try
            {
                await _eventBus.Publish(@event, cancellationToken).ConfigureAwait(false);
                results.Add(true);
            }
            catch (OperationCanceledException)
            {
                // Cancellation should stop batch processing immediately
                throw;
            }
            catch (Exception ex)
            {
                // Track failure but continue processing remaining events
                _logger?.LogWarning(ex, "Failed to publish event {EventType} in batch", @event.GetType().Name);
                results.Add(false);
            }
        }

        return results;
    }

    public async Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        Interlocked.Increment(ref _messagesQueued);
        await _queueProcessor.Enqueue(message, queueName, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        await _queueProcessor.StartQueue(queueName, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_queueProcessor == null)
            throw new InvalidOperationException("Queue functionality is not enabled. Call WithQueues() during configuration.");

        await _queueProcessor.StopQueue(queueName, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_outboxProcessor == null)
            throw new InvalidOperationException("Outbox functionality is not enabled. Call WithOutbox() during configuration.");

        Interlocked.Increment(ref _outboxMessages);
        await _outboxProcessor.PublishToOutbox(message, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_inboxProcessor == null)
            throw new InvalidOperationException("Inbox functionality is not enabled. Call WithInbox() during configuration.");

        Interlocked.Increment(ref _inboxMessages);
        await _inboxProcessor.ProcessIncoming(message, options, cancellationToken).ConfigureAwait(false);
    }

    public MessagingMetrics GetMetrics()
    {
        return new MessagingMetrics
        {
            CommandsSent = Interlocked.Read(ref _commandsSent),
            QueriesSent = Interlocked.Read(ref _queriesSent),
            EventsPublished = Interlocked.Read(ref _eventsPublished),
            MessagesQueued = Interlocked.Read(ref _messagesQueued),
            OutboxMessages = Interlocked.Read(ref _outboxMessages),
            InboxMessages = Interlocked.Read(ref _inboxMessages)
        };
    }

    public MessagingHealth GetHealth()
    {
        var now = _timeProvider.GetUtcNow();
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
