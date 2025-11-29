using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks.Dataflow;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Event bus implementation using the pipeline architecture.
/// Optimized for zero-allocation in steady state through caching and pooling.
/// </summary>
public class EventBus : IEventBus, IProcessor, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventBus> _logger;
    private readonly ActionBlock<EventEnvelope> _processingBlock;
    private readonly MessageProcessingPipelineBuilder _pipelineBuilder;

    // Pipeline cache per handler type to avoid rebuilding decorator chains
    private readonly ConcurrentDictionary<Type, Func<EventEnvelope, IMessageProcessor>> _pipelineFactoryCache = new();

    // Lightweight object pool for EventEnvelope using ConcurrentBag (zero dependencies)
    private readonly ConcurrentBag<EventEnvelope> _envelopePool = new();
    private const int MaxPoolSize = 64;
    private const int DefaultTaskArraySize = 8; // Most events have <8 handlers

    // Lock-free metrics using Interlocked
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
                BoundedCapacity = ProcessingConstants.EventBusBoundedCapacity
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
            // Get cached MethodInfo - avoids GetMethod() reflection on each call
            var handleMethod = HandlerTypeCache.GetHandleMethod(envelope.HandlerType);

            // Get or create pipeline factory for this handler type
            var pipelineFactory = GetOrCreatePipelineFactory(envelope.HandlerType, handleMethod);
            var pipeline = pipelineFactory(envelope);

            // Create processing context (struct - stack allocated)
            var context = new ProcessingContext
            {
                Component = "EventBus",
                Handler = envelope.Handler,
                HandlerType = envelope.HandlerType,
                Metadata = ImmutableDictionary<string, object>.Empty
                    .Add("EventType", envelope.Event.GetType().Name)
                    .Add("HandlerType", envelope.Handler.GetType().Name)
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
        return _envelopePool.TryTake(out var envelope) ? envelope : new EventEnvelope();
    }

    private void ReturnEnvelope(EventEnvelope envelope)
    {
        envelope.Reset();
        // Only return to pool if below max size to prevent unbounded growth
        if (_envelopePool.Count < MaxPoolSize)
        {
            _envelopePool.Add(envelope);
        }
    }

    private Func<EventEnvelope, IMessageProcessor> GetOrCreatePipelineFactory(Type handlerType, System.Reflection.MethodInfo handleMethod)
    {
        return _pipelineFactoryCache.GetOrAdd(handlerType, _ =>
        {
            // This factory closure captures the handleMethod, avoiding lookup per call
            return envelope =>
            {
                var coreProcessor = new CoreMessageProcessor(async (message, context, ct) =>
                {
                    await ((Task)handleMethod.Invoke(envelope.Handler, [envelope.Event, ct])!).ConfigureAwait(false);
                });

                return _pipelineBuilder.Build(coreProcessor);
            };
        });
    }

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
