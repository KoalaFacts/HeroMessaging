using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Event bus implementation using the pipeline architecture.
/// Caches handler dispatch while preserving per-event decorator lifetimes.
/// </summary>
public class EventBus : IEventBus, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly ActionBlock<EventEnvelope> _processingBlock;
    private readonly MessageProcessingPipelineBuilder _pipelineBuilder;
    private readonly int _maxPooledEnvelopes;

    private readonly ConcurrentDictionary<Type, IMessageProcessor> _coreProcessors = new();
    private readonly ConcurrentDictionary<(Type EventType, Type HandlerType), ImmutableDictionary<string, object>> _contextMetadata = new();

    // Lightweight object pool for EventEnvelope using ConcurrentBag (zero dependencies)
    private readonly ConcurrentBag<EventEnvelope> _envelopePool = [];
    private int _pooledEnvelopeCount;
    private const int DefaultTaskArraySize = 8; // Most events have <8 handlers

    // Lock-free metrics using Interlocked
    private long _publishedCount;
    private long _failedCount;
    private int _registeredHandlers;
    /// <summary>
    /// Initializes a new instance of the <see cref="EventBus"/> class.
    /// </summary>

    public EventBus(IServiceProvider serviceProvider, ILogger<EventBus>? logger = null)
        : this(serviceProvider, logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance with configurable in-process capacity limits.
    /// </summary>
    public EventBus(IServiceProvider serviceProvider, ILogger<EventBus>? logger = null, EventBusOptions? options = null)
    {
        var settings = EventBusSettings.Resolve(options ?? new EventBusOptions(), Environment.ProcessorCount);
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBus>.Instance;
        _pipelineBuilder = new MessageProcessingPipelineBuilder(serviceProvider);
        _maxPooledEnvelopes = settings.MaxPooledEnvelopes;

        // Configure default pipeline
        ConfigurePipeline();

        _processingBlock = new ActionBlock<EventEnvelope>(
            ProcessEventWithPipeline,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism,
                BoundedCapacity = settings.BoundedCapacity
            });
    }
    /// <summary>
    /// Gets or sets is running.
    /// </summary>

    public bool IsRunning { get; private set; } = true;
    /// <summary>
    /// Executes configure pipeline.
    /// </summary>

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
    /// <summary>
    /// Executes publish async.
    /// </summary>

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
    {
        // Return early if already cancelled - graceful handling
        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Publish cancelled before processing");
            return;
        }

        var eventType = @event.GetType();
        // Use cached handler type - avoids MakeGenericType allocation after first call
        var handlerType = HandlerTypeCache.GetEventHandlerType(eventType);

        // Enumerate handlers directly without ToList() allocation
        var handlers = _serviceProvider.GetServices(handlerType);
        var handlerCount = 0;

        // Use ArrayPool to avoid List allocation in hot path
        var taskArray = ArrayPool<Task<bool>>.Shared.Rent(DefaultTaskArraySize);
        try
        {
            foreach (var handler in handlers)
            {
                if (handler is null) continue;

                // Grow array if needed (rare case: >8 handlers)
                if (handlerCount >= taskArray.Length)
                {
                    var newArray = ArrayPool<Task<bool>>.Shared.Rent(taskArray.Length * 2);
                    Array.Copy(taskArray, newArray, handlerCount);
                    ArrayPool<Task<bool>>.Shared.Return(taskArray, clearArray: true);
                    taskArray = newArray;
                }

                // Get envelope from pool instead of allocating (if available)
                var envelope = RentEnvelope();
                envelope.Initialize(@event, handler, handlerType, cancellationToken);

                taskArray[handlerCount++] = _processingBlock.SendAsync(envelope, cancellationToken);
            }

            if (handlerCount == 0)
            {
                _logger.LogDebug("No handlers found for event type {EventType}", eventType.Name);
                return;
            }

            // Lock-free metrics update using Interlocked
            Interlocked.Increment(ref _publishedCount);
            Interlocked.Exchange(ref _registeredHandlers, handlerCount);

            // Wait for all tasks using the array segment
            for (var i = 0; i < handlerCount; i++)
            {
                await taskArray[i].ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<Task<bool>>.Shared.Return(taskArray, clearArray: true);
        }
    }

    private async Task ProcessEventWithPipeline(EventEnvelope envelope)
    {
        try
        {
            var coreProcessor = _coreProcessors.GetOrAdd(envelope.Event.GetType(), eventType =>
            {
                var invoker = EventHandlerInvokerCache.Get(eventType);
                return new CoreMessageProcessor((message, context, ct) =>
                    new ValueTask(invoker(context.Handler!, (IEvent)message, ct)));
            });
            var pipeline = _pipelineBuilder.Build(coreProcessor);

            // Create processing context (struct - stack allocated)
            var context = new ProcessingContext
            {
                Component = "EventBus",
                Handler = envelope.Handler,
                HandlerType = envelope.HandlerType,
                Metadata = _contextMetadata.GetOrAdd((envelope.Event.GetType(), envelope.Handler.GetType()),
                    static types => ImmutableDictionary<string, object>.Empty
                        .Add("EventType", types.EventType.Name)
                        .Add("HandlerType", types.HandlerType.Name))
            };

            // Process through the pipeline
            var result = await pipeline.ProcessAsync(envelope.Event, context, envelope.CancellationToken).ConfigureAwait(false);

            if (!result.Success && result.Exception != null)
            {
                // Lock-free failure count increment
                Interlocked.Increment(ref _failedCount);

                _logger.LogError(result.Exception,
                    "Failed to process event {EventType} with handler {HandlerType}: {Message}",
                    envelope.Event.GetType().Name,
                    envelope.Handler.GetType().Name,
                    result.Message);
            }
        }
        finally
        {
            // Return envelope to pool for reuse
            ReturnEnvelope(envelope);
        }
    }

    private EventEnvelope RentEnvelope()
    {
        if (!_envelopePool.TryTake(out var envelope))
            return new EventEnvelope();

        Interlocked.Decrement(ref _pooledEnvelopeCount);
        return envelope;
    }

    private void ReturnEnvelope(EventEnvelope envelope)
    {
        envelope.Reset();
        // Reserve capacity before publishing the envelope to concurrent renters.
        if (Interlocked.Increment(ref _pooledEnvelopeCount) <= _maxPooledEnvelopes)
            _envelopePool.Add(envelope);
        else
            Interlocked.Decrement(ref _pooledEnvelopeCount);
    }
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IEventBusMetrics GetMetrics()
    {
        // Lock-free reads using Interlocked.Read for 64-bit values
        return new EventBusMetrics
        {
            PublishedCount = Interlocked.Read(ref _publishedCount),
            FailedCount = Interlocked.Read(ref _failedCount),
            RegisteredHandlers = Volatile.Read(ref _registeredHandlers)
        };
    }

    private class EventBusMetrics : IEventBusMetrics
    {
        public long PublishedCount { get; init; }
        public long FailedCount { get; init; }
        public int RegisteredHandlers { get; init; }
    }

    /// <summary>
    /// Poolable envelope for event processing.
    /// Supports Initialize/Reset pattern for ObjectPool reuse.
    /// </summary>
    private class EventEnvelope
    {
        public IEvent Event { get; private set; } = null!;
        public object Handler { get; private set; } = null!;
        public Type HandlerType { get; private set; } = null!;
        public CancellationToken CancellationToken { get; private set; }

        public void Initialize(IEvent @event, object handler, Type handlerType, CancellationToken cancellationToken)
        {
            Event = @event;
            Handler = handler;
            HandlerType = handlerType;
            CancellationToken = cancellationToken;
        }

        public void Reset()
        {
            Event = null!;
            Handler = null!;
            HandlerType = null!;
            CancellationToken = default;
        }
    }

    /// <summary>
    /// Disposes the event bus by completing the processing block.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        IsRunning = false;
        _processingBlock.Complete();
        await _processingBlock.Completion.ConfigureAwait(false);
    }
}
