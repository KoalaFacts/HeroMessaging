using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Event bus implementation using the pipeline architecture
/// </summary>
public class EventBus : IEventBus, IProcessor
{
    /// <summary>Maximum number of events that can be queued for processing.</summary>
    private const int DefaultBoundedCapacity = 1000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly ActionBlock<EventEnvelope> _processingBlock;
    private readonly MessageProcessingPipelineBuilder _pipelineBuilder;

#if NET9_0_OR_GREATER
    private readonly Lock _metricsLock = new();
#else
    private readonly object _metricsLock = new();
#endif
    private long _publishedCount;
    private long _failedCount;
    private int _registeredHandlers;

    public EventBus(IServiceProvider serviceProvider, ILogger<EventBus>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBus>.Instance;
        _pipelineBuilder = new MessageProcessingPipelineBuilder(serviceProvider);

        // Configure default pipeline
        ConfigurePipeline();

        _processingBlock = new ActionBlock<EventEnvelope>(
            ProcessEventWithPipeline,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                BoundedCapacity = DefaultBoundedCapacity
            });
    }

    public bool IsRunning { get; private set; } = true;

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
        // Return early if already cancelled - graceful handling
        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Publish cancelled before processing");
            return;
        }

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

        await Task.WhenAll(tasks).ConfigureAwait(false);
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

            await ((Task)handleMethod.Invoke(envelope.Handler, [envelope.Event, ct])!).ConfigureAwait(false);
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
        var result = await pipeline.ProcessAsync(envelope.Event, context, envelope.CancellationToken).ConfigureAwait(false);

        if (!result.Success && result.Exception != null)
        {
            lock (_metricsLock)
            {
                _failedCount++;
            }

            _logger.LogError(result.Exception,
                "Failed to process event {EventType} with handler {HandlerType}: {Message}",
                envelope.Event.GetType().Name,
                envelope.Handler.GetType().Name,
                result.Message);
        }
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

    private class EventBusMetrics : IEventBusMetrics
    {
        public long PublishedCount { get; init; }
        public long FailedCount { get; init; }
        public int RegisteredHandlers { get; init; }
    }

    private class EventEnvelope
    {
        public IEvent Event { get; set; } = null!;
        public object Handler { get; set; } = null!;
        public Type HandlerType { get; set; } = null!;
        public CancellationToken CancellationToken { get; set; }
    }
}