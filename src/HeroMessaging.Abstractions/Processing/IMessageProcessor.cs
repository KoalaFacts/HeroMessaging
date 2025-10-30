using HeroMessaging.Abstractions.Messages;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// High-performance message processor interface for processing messages through the HeroMessaging pipeline.
/// Uses ValueTask for zero-allocation async operations in hot paths.
/// </summary>
/// <remarks>
/// Implement this interface to create custom message processors that integrate with the HeroMessaging framework.
/// The processor is responsible for executing the business logic associated with a message.
///
/// Design Principles:
/// - Uses ValueTask for high-performance async operations (reduces heap allocations)
/// - Receives immutable ProcessingContext for thread-safe operation
/// - Returns ProcessingResult struct to avoid allocations
/// - Supports cancellation for responsive shutdown
///
/// Typical implementations:
/// - Command processors (execute commands and return results)
/// - Event processors (handle domain events)
/// - Query processors (retrieve and return data)
/// - Validation processors (validate messages before processing)
/// - Retry processors (implement retry logic with exponential backoff)
/// - Circuit breaker processors (prevent cascading failures)
///
/// <code>
/// public class OrderCommandProcessor : IMessageProcessor
/// {
///     private readonly IOrderRepository _repository;
///     private readonly ILogger&lt;OrderCommandProcessor&gt; _logger;
///
///     public OrderCommandProcessor(IOrderRepository repository, ILogger&lt;OrderCommandProcessor&gt; logger)
///     {
///         _repository = repository;
///         _logger = logger;
///     }
///
///     public async ValueTask&lt;ProcessingResult&gt; ProcessAsync(
///         IMessage message,
///         ProcessingContext context,
///         CancellationToken cancellationToken)
///     {
///         try
///         {
///             if (message is CreateOrderCommand cmd)
///             {
///                 var order = await _repository.CreateOrderAsync(cmd.CustomerId, cmd.Amount, cancellationToken);
///                 _logger.LogInformation("Order {OrderId} created", order.Id);
///
///                 return ProcessingResult.Successful($"Order {order.Id} created", order);
///             }
///
///             return ProcessingResult.Failed(
///                 new InvalidOperationException($"Unknown message type: {message.GetType().Name}")
///             );
///         }
///         catch (Exception ex)
///         {
///             _logger.LogError(ex, "Failed to process order command");
///             return ProcessingResult.Failed(ex, "Failed to create order");
///         }
///     }
/// }
/// </code>
/// </remarks>
public interface IMessageProcessor
{
    /// <summary>
    /// Processes a message through the messaging pipeline with the provided context.
    /// </summary>
    /// <param name="message">The message to process. Must not be null.</param>
    /// <param name="context">The processing context containing metadata, retry information, and handler details.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A ValueTask containing the ProcessingResult indicating success or failure.
    /// Use ProcessingResult.Successful() for success or ProcessingResult.Failed() for failures.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method should:
    /// - Execute the core business logic for the message
    /// - Return success/failure via ProcessingResult (do not throw exceptions for business failures)
    /// - Only throw exceptions for catastrophic failures (infrastructure errors)
    /// - Respect the cancellation token for responsive shutdown
    /// - Use the context for retry logic, metadata access, and diagnostics
    ///
    /// Performance considerations:
    /// - Implementation should complete in &lt;1ms for optimal throughput
    /// - Avoid allocations in hot paths
    /// - Use async I/O for database/network operations
    /// - Consider using object pools for frequently allocated objects
    /// </remarks>
    ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable processing context that carries metadata, retry information, and handler details through the message processing pipeline.
/// Implemented as a readonly record struct to minimize heap allocations and maximize performance.
/// </summary>
/// <remarks>
/// The ProcessingContext is designed for high-performance scenarios:
/// - Implemented as struct (stack allocated, zero GC pressure)
/// - Immutable via record struct semantics
/// - Uses ImmutableDictionary for thread-safe metadata
/// - Provides fluent API for creating modified contexts
///
/// Use cases:
/// - Passing component/handler information through the pipeline
/// - Tracking retry attempts and failure timing
/// - Storing request-scoped metadata (correlation IDs, user context, etc.)
/// - Enabling middleware and decorators to share state
///
/// <code>
/// // Creating a context with metadata
/// var context = new ProcessingContext("OrderProcessor")
///     .WithMetadata("CorrelationId", Guid.NewGuid())
///     .WithMetadata("UserId", "user-123");
///
/// // Accessing metadata
/// var correlationId = context.GetMetadataReference&lt;Guid&gt;("CorrelationId");
///
/// // Tracking retries
/// var retryContext = context.WithRetry(retryCount: 1, firstFailureTime: DateTime.UtcNow);
/// </code>
/// </remarks>
public readonly record struct ProcessingContext
{
    /// <summary>
    /// Initializes a new instance of ProcessingContext with default values.
    /// All properties are set to their default values (empty strings, nulls, zero).
    /// </summary>
    public ProcessingContext()
    {
        Component = string.Empty;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Initializes a new instance of ProcessingContext with the specified component and optional metadata.
    /// </summary>
    /// <param name="component">The name of the component processing the message (e.g., "OrderProcessor", "CommandHandler").</param>
    /// <param name="metadata">Optional metadata dictionary to initialize the context with.</param>
    public ProcessingContext(string component, ImmutableDictionary<string, object>? metadata = null)
    {
        Component = component;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = metadata ?? ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Gets the name of the component processing the message.
    /// Typically the class name of the processor or handler.
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Logging and diagnostics (identify which component processed a message)
    /// - Metrics and observability (track performance per component)
    /// - Error reporting (provide context in error messages)
    /// </remarks>
    public string Component { get; init; }

    /// <summary>
    /// Gets the handler instance processing the message.
    /// May be null if no specific handler is associated.
    /// </summary>
    /// <remarks>
    /// Used by the framework to track the actual handler instance for:
    /// - Dependency injection resolution
    /// - Lifetime management
    /// - Handler-specific configuration
    /// </remarks>
    public object? Handler { get; init; }

    /// <summary>
    /// Gets the type of the handler processing the message.
    /// May be null if no specific handler type is associated.
    /// </summary>
    /// <remarks>
    /// Used for:
    /// - Type-based routing and filtering
    /// - Reflection-based processing
    /// - Handler discovery and registration
    /// </remarks>
    public Type? HandlerType { get; init; }

    /// <summary>
    /// Gets the number of times this message has been retried.
    /// Zero indicates first attempt, positive values indicate retry attempts.
    /// </summary>
    /// <remarks>
    /// Used to implement retry policies:
    /// - Exponential backoff calculations
    /// - Maximum retry limits
    /// - Retry-specific logging and metrics
    /// - Moving to dead-letter queue after max retries
    /// </remarks>
    public int RetryCount { get; init; }

    /// <summary>
    /// Gets the timestamp when this message first failed processing.
    /// Null if the message has not failed yet.
    /// </summary>
    /// <remarks>
    /// Used to:
    /// - Calculate total time spent retrying
    /// - Implement time-based retry policies
    /// - Detect messages stuck in retry loops
    /// - Provide SLA metrics (time to success/failure)
    /// </remarks>
    public DateTime? FirstFailureTime { get; init; }

    /// <summary>
    /// Gets the immutable metadata dictionary containing custom key-value pairs.
    /// Metadata is preserved across retries and through the processing pipeline.
    /// </summary>
    /// <remarks>
    /// Common metadata keys:
    /// - "CorrelationId": Request correlation identifier
    /// - "UserId": User or tenant identifier
    /// - "TraceId": Distributed tracing identifier
    /// - "Source": Origin of the message
    /// - "Priority": Message priority level
    ///
    /// Use WithMetadata() to add new metadata items (creates a new context).
    /// Use GetMetadata() or GetMetadataReference() to retrieve values.
    /// </remarks>
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Creates a new ProcessingContext with the specified metadata added or updated.
    /// Does not modify the current instance (immutable).
    /// </summary>
    /// <param name="key">The metadata key. Must not be null or empty.</param>
    /// <param name="value">The metadata value. Must not be null.</param>
    /// <returns>A new ProcessingContext instance with the metadata added/updated.</returns>
    /// <remarks>
    /// This method uses the immutable record struct's 'with' expression to create
    /// a new instance with modified metadata. The original context is unchanged.
    ///
    /// <code>
    /// var context = new ProcessingContext("OrderProcessor");
    /// var updatedContext = context
    ///     .WithMetadata("CorrelationId", Guid.NewGuid())
    ///     .WithMetadata("UserId", "user-123");
    /// // original 'context' is unchanged
    /// </code>
    /// </remarks>
    public ProcessingContext WithMetadata(string key, object value)
    {
        return this with { Metadata = Metadata.SetItem(key, value) };
    }

    /// <summary>
    /// Creates a new ProcessingContext with updated retry information.
    /// Does not modify the current instance (immutable).
    /// </summary>
    /// <param name="retryCount">The new retry count (typically incremented from previous value).</param>
    /// <param name="firstFailureTime">
    /// The timestamp when the message first failed. If null, preserves the existing FirstFailureTime
    /// (use this on subsequent retries to maintain the original failure time).
    /// </param>
    /// <returns>A new ProcessingContext instance with updated retry information.</returns>
    /// <remarks>
    /// Use this method when implementing retry logic:
    ///
    /// <code>
    /// // First failure - set the failure time
    /// var failedContext = context.WithRetry(1, DateTime.UtcNow);
    ///
    /// // Subsequent retries - preserve the original failure time
    /// var retriedContext = failedContext.WithRetry(2);
    ///
    /// // Calculate retry delay with exponential backoff
    /// var delay = TimeSpan.FromSeconds(Math.Pow(2, retriedContext.RetryCount));
    /// </code>
    /// </remarks>
    public ProcessingContext WithRetry(int retryCount, DateTime? firstFailureTime = null)
    {
        return this with
        {
            RetryCount = retryCount,
            FirstFailureTime = firstFailureTime ?? FirstFailureTime
        };
    }

    /// <summary>
    /// Retrieves a strongly-typed value type from the metadata dictionary.
    /// Returns null if the key doesn't exist or the value is not of the expected type.
    /// </summary>
    /// <typeparam name="T">The value type to retrieve. Must be a struct.</typeparam>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>
    /// The metadata value cast to type T if found and compatible, otherwise null.
    /// Returns nullable T to indicate absence of value.
    /// </returns>
    /// <remarks>
    /// This method is optimized for value types (structs) such as int, Guid, DateTime, etc.
    /// For reference types (classes), use GetMetadataReference() instead.
    ///
    /// Marked with AggressiveInlining for maximum performance in hot paths.
    ///
    /// <code>
    /// var priority = context.GetMetadata&lt;int&gt;("Priority");
    /// if (priority.HasValue)
    /// {
    ///     Console.WriteLine($"Priority: {priority.Value}");
    /// }
    ///
    /// var timestamp = context.GetMetadata&lt;DateTime&gt;("Timestamp");
    /// </code>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadata<T>(string key) where T : struct
    {
        return Metadata.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default(T?);
    }

    /// <summary>
    /// Retrieves a strongly-typed reference type from the metadata dictionary.
    /// Returns null if the key doesn't exist or the value is not of the expected type.
    /// </summary>
    /// <typeparam name="T">The reference type to retrieve. Must be a class.</typeparam>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>
    /// The metadata value cast to type T if found and compatible, otherwise null.
    /// </returns>
    /// <remarks>
    /// This method is optimized for reference types (classes) such as string, custom objects, etc.
    /// For value types (structs), use GetMetadata() instead.
    ///
    /// Marked with AggressiveInlining for maximum performance in hot paths.
    ///
    /// <code>
    /// var userId = context.GetMetadataReference&lt;string&gt;("UserId");
    /// if (userId != null)
    /// {
    ///     Console.WriteLine($"User: {userId}");
    /// }
    ///
    /// var userContext = context.GetMetadataReference&lt;UserContext&gt;("UserContext");
    /// </code>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadataReference<T>(string key) where T : class
    {
        return Metadata.TryGetValue(key, out var value) ? value as T : null;
    }
}

/// <summary>
/// Represents the result of message processing operations.
/// Implemented as a readonly record struct to minimize heap allocations and maximize throughput.
/// </summary>
/// <remarks>
/// ProcessingResult is designed for high-performance message processing:
/// - Implemented as struct (stack allocated, zero GC pressure in steady state)
/// - Immutable via record struct semantics
/// - Factory methods for creating success/failure results
/// - Supports optional result data and error information
///
/// Use this type instead of throwing exceptions for expected failures:
/// - Throwing exceptions is expensive (stack unwinding, allocation)
/// - Results are explicit and predictable
/// - Supports telemetry and metrics tracking
/// - Enables retry logic without exception handling overhead
///
/// <code>
/// // Success with data
/// var result = ProcessingResult.Successful("Order created", orderId);
///
/// // Success without data
/// var result = ProcessingResult.Successful();
///
/// // Failure with exception
/// try
/// {
///     await SaveOrderAsync(order);
///     return ProcessingResult.Successful();
/// }
/// catch (DbException ex)
/// {
///     return ProcessingResult.Failed(ex, "Failed to save order to database");
/// }
///
/// // Check result
/// if (result.Success)
/// {
///     logger.LogInformation("Processing succeeded: {Message}", result.Message);
/// }
/// else
/// {
///     logger.LogError(result.Exception, "Processing failed: {Message}", result.Message);
/// }
/// </code>
/// </remarks>
public readonly record struct ProcessingResult
{
    /// <summary>
    /// Gets a value indicating whether the processing operation succeeded.
    /// True for success, false for failure.
    /// </summary>
    /// <remarks>
    /// Always check this property before accessing Data or Exception:
    /// - If true: Data may contain result, Exception is null
    /// - If false: Exception contains error details, Data may be null
    /// </remarks>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the exception that caused the processing to fail.
    /// Null if the operation succeeded.
    /// </summary>
    /// <remarks>
    /// This property is only populated when Success is false.
    /// Use this for:
    /// - Error logging and diagnostics
    /// - Determining retry eligibility (transient vs permanent errors)
    /// - Extracting stack traces for debugging
    /// - Propagating errors up the call stack if needed
    /// </remarks>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets a human-readable message describing the processing result.
    /// May be set for both success and failure cases.
    /// </summary>
    /// <remarks>
    /// Use this for:
    /// - Success: Confirmation message (e.g., "Order 123 created successfully")
    /// - Failure: User-friendly error message (e.g., "Database connection failed")
    /// - Logging and diagnostics
    /// - UI feedback
    ///
    /// This should be a user-friendly message, not a technical error dump.
    /// Technical details should be in the Exception property.
    /// </remarks>
    public string? Message { get; init; }

    /// <summary>
    /// Gets optional data returned from the processing operation.
    /// Typically used for success cases to return created/updated entities or identifiers.
    /// </summary>
    /// <remarks>
    /// Common uses:
    /// - Created entity ID (e.g., order ID, customer ID)
    /// - Updated entity
    /// - Query results
    /// - Calculated values
    ///
    /// Note: For typed results, consider creating a custom result type or using
    /// generic wrappers. This property uses object for maximum flexibility.
    ///
    /// <code>
    /// var result = ProcessingResult.Successful("Order created", orderId: "ORD-123");
    /// if (result.Success &amp;&amp; result.Data is string orderId)
    /// {
    ///     Console.WriteLine($"Created order: {orderId}");
    /// }
    /// </code>
    /// </remarks>
    public object? Data { get; init; }

    /// <summary>
    /// Creates a successful processing result with optional message and data.
    /// </summary>
    /// <param name="message">Optional human-readable success message.</param>
    /// <param name="data">Optional data to return (e.g., created entity ID, result object).</param>
    /// <returns>A ProcessingResult with Success = true and the provided message/data.</returns>
    /// <remarks>
    /// Use this factory method to indicate successful processing:
    ///
    /// <code>
    /// // Simple success
    /// return ProcessingResult.Successful();
    ///
    /// // Success with message
    /// return ProcessingResult.Successful("Customer profile updated");
    ///
    /// // Success with data
    /// var orderId = await CreateOrderAsync(command);
    /// return ProcessingResult.Successful("Order created", orderId);
    /// </code>
    ///
    /// Marked with AggressiveInlining for optimal performance in hot paths.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Successful(string? message = null, object? data = null)
        => new() { Success = true, Message = message, Data = data };

    /// <summary>
    /// Creates a failed processing result with the exception that caused the failure.
    /// </summary>
    /// <param name="exception">The exception that caused the processing to fail. Must not be null.</param>
    /// <param name="message">Optional human-readable error message. If null, uses exception.Message.</param>
    /// <returns>A ProcessingResult with Success = false and the provided exception/message.</returns>
    /// <remarks>
    /// Use this factory method to indicate processing failure:
    ///
    /// <code>
    /// try
    /// {
    ///     await ProcessOrderAsync(order);
    ///     return ProcessingResult.Successful();
    /// }
    /// catch (DatabaseException ex)
    /// {
    ///     // Failure with custom message
    ///     return ProcessingResult.Failed(ex, "Failed to save order to database");
    /// }
    /// catch (ValidationException ex)
    /// {
    ///     // Failure using exception message
    ///     return ProcessingResult.Failed(ex);
    /// }
    /// </code>
    ///
    /// Marked with AggressiveInlining for optimal performance in hot paths.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Failed(Exception exception, string? message = null)
        => new() { Success = false, Exception = exception, Message = message };
}