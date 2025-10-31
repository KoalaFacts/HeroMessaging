using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;

namespace HeroMessaging.Processing;

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
    private readonly TimeProvider _timeProvider;

    public bool IsRunning { get; private set; } = true;

    public EventBus(IServiceProvider serviceProvider, TimeProvider timeProvider, ILogger<EventBus>? logger = null, IErrorHandler? errorHandler = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EventBus>.Instance;
        _errorHandler = errorHandler;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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
        CompatibilityHelpers.ThrowIfNull(@event, nameof(@event));

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
                Handler = handler!,
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
                        FirstFailureTime = retryCount == 0 ? _timeProvider.GetUtcNow().DateTime : envelope.FirstFailureTime ?? _timeProvider.GetUtcNow().DateTime,
                        LastFailureTime = _timeProvider.GetUtcNow().DateTime,
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
                            envelope.FirstFailureTime ??= _timeProvider.GetUtcNow().DateTime;
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "This method performs computation (locking and object creation) and is not a simple getter")]
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

/// <summary>
/// Publishes domain events to multiple registered event handlers with support for parallel processing and automatic retry.
/// </summary>
/// <remarks>
/// The event bus implements the Publish-Subscribe pattern for distributing domain events to interested subscribers.
/// Unlike commands (1 handler) and queries (1 handler), events support zero to many handlers (0..N).
///
/// Design Principles:
/// - Events represent something that has happened (past tense)
/// - Fire-and-forget semantics (no return values)
/// - Multiple handlers can process the same event independently
/// - Parallel handler execution for maximum throughput
/// - Automatic retry with exponential backoff
/// - Optional error handler integration for advanced error handling
///
/// Event Characteristics:
/// - IEvent: All events implement this interface
/// - Immutable: Events should be immutable records of what happened
/// - Domain-driven: Events represent significant domain occurrences
/// - Asynchronous: All handlers execute asynchronously
/// - Independent: Handler failures don't affect other handlers
///
/// Processing Characteristics:
/// - Parallel execution (MaxDegreeOfParallelism = CPU count)
/// - Bounded queue capacity (1000 events)
/// - Automatic retry up to 3 attempts per handler
/// - Exponential backoff retry strategy
/// - Individual handler failure isolation
/// - Automatic metrics tracking (published count, failures, handler count)
///
/// <code>
/// // Define an event
/// public record OrderCreatedEvent : IEvent
/// {
///     public Guid OrderId { get; init; }
///     public Guid CustomerId { get; init; }
///     public decimal TotalAmount { get; init; }
///     public DateTime CreatedAt { get; init; }
/// }
///
/// // Define event handlers (can have multiple)
/// public class SendOrderConfirmationEmailHandler : IEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     private readonly IEmailService _emailService;
///
///     public SendOrderConfirmationEmailHandler(IEmailService emailService)
///     {
///         _emailService = emailService;
///     }
///
///     public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
///     {
///         await _emailService.SendOrderConfirmationAsync(@event.OrderId, cancellationToken);
///     }
/// }
///
/// public class UpdateInventoryHandler : IEventHandler&lt;OrderCreatedEvent&gt;
/// {
///     private readonly IInventoryService _inventoryService;
///
///     public UpdateInventoryHandler(IInventoryService inventoryService)
///     {
///         _inventoryService = inventoryService;
///     }
///
///     public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
///     {
///         await _inventoryService.ReserveInventoryAsync(@event.OrderId, cancellationToken);
///     }
/// }
///
/// // Usage
/// var eventBus = serviceProvider.GetRequiredService&lt;IEventBus&gt;();
/// await eventBus.Publish(new OrderCreatedEvent
/// {
///     OrderId = Guid.NewGuid(),
///     CustomerId = customerId,
///     TotalAmount = 150.00m,
///     CreatedAt = DateTime.UtcNow
/// });
///
/// // Both handlers execute in parallel, independently
/// // If one fails, it retries without affecting the other
///
/// // Monitor event bus
/// var metrics = eventBus.GetMetrics();
/// logger.LogInformation(
///     "Event Bus: {Published} events, {Failed} failures, {Handlers} handlers",
///     metrics.PublishedCount,
///     metrics.FailedCount,
///     metrics.RegisteredHandlers
/// );
/// </code>
/// </remarks>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered event handlers for parallel processing.
    /// </summary>
    /// <param name="event">The event to publish. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the event has been queued for all handlers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when event is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method:
    /// - Resolves all registered IEventHandler&lt;TEvent&gt; instances from the service provider
    /// - Queues the event for each handler independently
    /// - Returns immediately after queueing (doesn't wait for handlers to complete)
    /// - Logs a debug message if no handlers are registered (not an error)
    /// - Tracks publishing metrics
    ///
    /// Processing Behavior:
    /// - All handlers execute in parallel (up to CPU count concurrent handlers)
    /// - Each handler has independent retry logic (up to 3 attempts)
    /// - Handler failures are isolated (one handler's failure doesn't affect others)
    /// - Automatic exponential backoff between retries (2^retryCount seconds)
    /// - IErrorHandler integration for custom error handling policies
    /// - Failed handlers are logged with retry attempt information
    ///
    /// Error Handling:
    /// - If IErrorHandler is registered, it determines retry/discard/escalate behavior
    /// - If no IErrorHandler, uses default retry policy (3 attempts with exponential backoff)
    /// - ErrorAction.Retry: Retry with optional custom delay
    /// - ErrorAction.SendToDeadLetter: Stop retrying, mark as failed
    /// - ErrorAction.Discard: Stop retrying, discard event
    /// - ErrorAction.Escalate: Rethrow exception (critical errors)
    ///
    /// Performance Considerations:
    /// - Event queueing is async and returns quickly
    /// - Handlers execute in parallel for maximum throughput
    /// - Bounded capacity (1000) prevents memory exhaustion
    /// - Each handler maintains retry state independently
    /// - Metrics tracking adds minimal overhead
    ///
    /// Best Practices:
    /// - Keep event handlers focused and fast
    /// - Use idempotent handlers (events may be retried)
    /// - Don't throw exceptions for expected business failures
    /// - Use IErrorHandler for custom error handling
    /// - Consider eventual consistency (handlers may complete at different times)
    /// - Log handler execution for observability
    ///
    /// <code>
    /// // Simple event publishing
    /// await eventBus.Publish(new CustomerRegisteredEvent
    /// {
    ///     CustomerId = Guid.NewGuid(),
    ///     Email = "customer@example.com",
    ///     RegisteredAt = DateTime.UtcNow
    /// });
    ///
    /// // Publishing with cancellation
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    /// try
    /// {
    ///     await eventBus.Publish(new PaymentProcessedEvent
    ///     {
    ///         PaymentId = paymentId,
    ///         Amount = 99.99m
    ///     }, cts.Token);
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     logger.LogWarning("Event publishing cancelled");
    /// }
    ///
    /// // Publishing in a loop (all execute in parallel)
    /// foreach (var item in orderItems)
    /// {
    ///     await eventBus.Publish(new OrderItemAddedEvent
    ///     {
    ///         OrderId = orderId,
    ///         ProductId = item.ProductId,
    ///         Quantity = item.Quantity
    ///     });
    /// }
    ///
    /// // No handlers registered (not an error, just logged)
    /// await eventBus.Publish(new UnhandledEvent()); // Logs debug message
    /// </code>
    /// </remarks>
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
}