using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

/// <summary>
/// Event bus implementation using the pipeline architecture
/// </summary>
public class EventBusV2 : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBusV2> _logger;
    private readonly ActionBlock<EventEnvelope> _processingBlock;
    private readonly MessageProcessingPipelineBuilder _pipelineBuilder;

    public EventBusV2(IServiceProvider serviceProvider, ILogger<EventBusV2>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBusV2>.Instance;
        _pipelineBuilder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Configure default pipeline
        ConfigurePipeline();

        _processingBlock = new ActionBlock<EventEnvelope>(
            ProcessEventWithPipeline,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = 1000
            });
    }

    private void ConfigurePipeline()
    {
        _pipelineBuilder
            .UseMetrics()           // Outermost - collect metrics for everything
            .UseLogging()           // Log the entire process
            .UseCorrelation()       // Track correlation/causation for choreography
            .UseValidation()        // Validate before processing
            .UseErrorHandling()     // Handle errors with dead letter queue
            .UseRetry();            // Innermost - retry the actual processing
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

        // Process each handler in parallel
        var tasks = handlers.Select(handler =>
        {
            var envelope = new EventEnvelope
            {
                Event = @event,
                Handler = handler!,
                HandlerType = handlerType,
                CancellationToken = cancellationToken
            };

            return _processingBlock.SendAsync(envelope, cancellationToken);
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessEventWithPipeline(EventEnvelope envelope)
    {
        // Create the core processor that executes the handler
        var coreProcessor = new CoreMessageProcessor(async (message, context, ct) =>
        {
            var handleMethod = envelope.HandlerType.GetMethod("Handle");
            if (handleMethod == null)
            {
                throw new InvalidOperationException($"Handle method not found on {envelope.HandlerType.Name}");
            }

            await (Task)handleMethod.Invoke(envelope.Handler, [envelope.Event, ct])!;
        });

        // Build the pipeline
        var pipeline = _pipelineBuilder.Build(coreProcessor);

        // Create processing context
        var context = new ProcessingContext
        {
            Component = "EventBus",
            Handler = envelope.Handler,
            HandlerType = envelope.HandlerType
        }
        .WithMetadata("EventType", envelope.Event.GetType().Name)
        .WithMetadata("HandlerType", envelope.Handler.GetType().Name);

        // Process through the pipeline
        var result = await pipeline.ProcessAsync(envelope.Event, context, envelope.CancellationToken);

        if (!result.Success && result.Exception != null)
        {
            _logger.LogError(result.Exception,
                "Failed to process event {EventType} with handler {HandlerType}: {Message}",
                envelope.Event.GetType().Name,
                envelope.Handler.GetType().Name,
                result.Message);
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