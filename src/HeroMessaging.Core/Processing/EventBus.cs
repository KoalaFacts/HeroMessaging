using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class EventBus : IEventBus, IProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly IErrorHandler? _errorHandler;
    private readonly ActionBlock<EventEnvelope> _processingBlock;
    private long _publishedCount;
    private long _failedCount;
    private int _registeredHandlers;
    private readonly object _metricsLock = new();
    
    public bool IsRunning { get; private set; } = true;

    public EventBus(IServiceProvider serviceProvider, ILogger<EventBus> logger, IErrorHandler? errorHandler = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _errorHandler = errorHandler;
        
        _processingBlock = new ActionBlock<EventEnvelope>(
            ProcessEvent,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = 1000
            });
    }

    public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        var eventType = @event.GetType();
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handlers = _serviceProvider.GetServices(handlerType).ToList();
        
        if (!handlers.Any())
        {
            _logger.LogDebug("No handlers found for event type {EventType}", eventType.Name);
            return;
        }
        
        lock (_metricsLock)
        {
            _publishedCount++;
            _registeredHandlers = handlers.Count;
        }

        var tasks = new List<Task>();
        
        foreach (var handler in handlers)
        {
            var envelope = new EventEnvelope
            {
                Event = @event,
                Handler = handler,
                HandlerType = handlerType,
                CancellationToken = cancellationToken
            };
            
            await _processingBlock.SendAsync(envelope, cancellationToken);
        }
    }

    private async Task ProcessEvent(EventEnvelope envelope)
    {
        var retryCount = 0;
        var maxRetries = 3;
        
        while (retryCount <= maxRetries)
        {
            try
            {
                var handleMethod = envelope.HandlerType.GetMethod("Handle");
                await (Task)handleMethod!.Invoke(envelope.Handler, [envelope.Event, envelope.CancellationToken])!;
                return; // Success - exit
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event {EventType} with handler {HandlerType}. Attempt {RetryCount}/{MaxRetries}", 
                    envelope.Event.GetType().Name, 
                    envelope.Handler.GetType().Name,
                    retryCount + 1,
                    maxRetries + 1);
                
                if (_errorHandler != null)
                {
                    var context = new ErrorContext
                    {
                        RetryCount = retryCount,
                        MaxRetries = maxRetries,
                        Component = "EventBus",
                        FirstFailureTime = retryCount == 0 ? DateTime.UtcNow : envelope.FirstFailureTime ?? DateTime.UtcNow,
                        LastFailureTime = DateTime.UtcNow,
                        Metadata = new Dictionary<string, object>
                        {
                            ["EventType"] = envelope.Event.GetType().Name,
                            ["HandlerType"] = envelope.Handler.GetType().Name
                        }
                    };
                    
                    var result = await _errorHandler.HandleError(envelope.Event, ex, context, envelope.CancellationToken);
                    
                    switch (result.Action)
                    {
                        case ErrorAction.Retry:
                            retryCount++;
                            if (result.RetryDelay.HasValue)
                                await Task.Delay(result.RetryDelay.Value, envelope.CancellationToken);
                            envelope.FirstFailureTime ??= DateTime.UtcNow;
                            continue;
                            
                        case ErrorAction.SendToDeadLetter:
                        case ErrorAction.Discard:
                            lock (_metricsLock)
                            {
                                _failedCount++;
                            }
                            
                            _logger.LogWarning("Event {EventType} processing failed and was {Action}: {Reason}", 
                                envelope.Event.GetType().Name, result.Action, result.Reason);
                            return;
                            
                        case ErrorAction.Escalate:
                            _logger.LogCritical(ex, "Critical error processing event {EventType}. Escalating.", 
                                envelope.Event.GetType().Name);
                            throw;
                    }
                }
                else
                {
                    // No error handler - just log and continue
                    if (retryCount >= maxRetries)
                    {
                        lock (_metricsLock)
                        {
                            _failedCount++;
                        }
                        
                        _logger.LogError("Event {EventType} processing failed after {MaxRetries} retries", 
                            envelope.Event.GetType().Name, maxRetries);
                        return;
                    }
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), envelope.CancellationToken);
                }
            }
        }
    }

    private class EventEnvelope
    {
        public IEvent Event { get; set; } = null!;
        public object Handler { get; set; } = null!;
        public Type HandlerType { get; set; } = null!;
        public CancellationToken CancellationToken { get; set; }
        public DateTime? FirstFailureTime { get; set; }
    }
    
    public IEventBusMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new EventBusMetrics
            {
                PublishedCount = _publishedCount,
                FailedCount = _failedCount,
                RegisteredHandlers = _registeredHandlers
            };
        }
    }
}

public class EventBusMetrics : IEventBusMetrics
{
    public long PublishedCount { get; init; }
    public long FailedCount { get; init; }
    public int RegisteredHandlers { get; init; }
}

public interface IEventBus
{
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
}