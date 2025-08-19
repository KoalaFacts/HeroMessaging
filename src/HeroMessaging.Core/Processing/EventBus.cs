using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

public class EventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly ActionBlock<EventEnvelope> _processingBlock;

    public EventBus(IServiceProvider serviceProvider, ILogger<EventBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        _processingBlock = new ActionBlock<EventEnvelope>(
            async envelope => await ProcessEvent(envelope),
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
        try
        {
            var handleMethod = envelope.HandlerType.GetMethod("Handle");
            await (Task)handleMethod!.Invoke(envelope.Handler, new object[] { envelope.Event, envelope.CancellationToken })!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventType} with handler {HandlerType}", 
                envelope.Event.GetType().Name, 
                envelope.Handler.GetType().Name);
        }
    }

    private class EventEnvelope
    {
        public IEvent Event { get; set; } = null!;
        public object Handler { get; set; } = null!;
        public Type HandlerType { get; set; } = null!;
        public CancellationToken CancellationToken { get; set; }
    }
}

public interface IEventBus
{
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
}