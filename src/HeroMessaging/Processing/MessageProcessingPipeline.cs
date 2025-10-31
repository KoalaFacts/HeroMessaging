using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Abstractions.Policies;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Fluent builder for creating message processing pipelines with decorator-based cross-cutting concerns.
/// </summary>
/// <param name="serviceProvider">The service provider for resolving decorator dependencies.</param>
/// <remarks>
/// This builder enables composing message processing pipelines using the Decorator pattern.
/// Each decorator wraps the previous processor, forming a chain where each layer can:
/// - Execute logic before the inner processor
/// - Execute logic after the inner processor
/// - Modify the message or context
/// - Handle errors from inner processors
/// - Short-circuit the pipeline
///
/// Decorator Execution Order:
/// - Decorators execute in REVERSE order of how they're added
/// - First added decorator executes LAST (outermost layer)
/// - Last added decorator executes FIRST (innermost layer)
/// - Example: UseMetrics().UseLogging() → Metrics wraps Logging
///
/// Available Decorators:
/// - UseLogging: Structured logging with configurable log levels
/// - UseValidation: Message validation before processing
/// - UseRetry: Automatic retry with configurable policy
/// - UseErrorHandling: Error handling with dead-letter queue support
/// - UseMetrics: Metrics collection and reporting
/// - UseCircuitBreaker: Circuit breaker for fault tolerance
/// - UseCorrelation: Correlation and causation ID tracking
/// - UseOpenTelemetry: Distributed tracing integration
/// - Use: Custom decorator injection
///
/// Usage Pattern:
/// 1. Create builder with service provider
/// 2. Add decorators in desired order (outermost to innermost)
/// 3. Call Build() with core processor
/// 4. Use resulting processor for message handling
///
/// Thread Safety:
/// Builder is not thread-safe. Build pipeline once, then use processor from multiple threads.
/// </remarks>
public class MessageProcessingPipelineBuilder(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly List<Func<IMessageProcessor, IMessageProcessor>> _decorators = new();


    /// <summary>
    /// Add logging to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseLogging(LogLevel successLevel = LogLevel.Debug, bool logPayload = false)
    {
        _decorators.Add(processor =>
        {
            var logger = _serviceProvider.GetService<ILogger<LoggingDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggingDecorator>.Instance;
            return new LoggingDecorator(processor, logger, successLevel, logPayload);
        });
        return this;
    }

    /// <summary>
    /// Add validation to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseValidation(IMessageValidator? validator = null)
    {
        _decorators.Add(processor =>
        {
            var validatorToUse = validator ?? _serviceProvider.GetService<IMessageValidator>();
            if (validatorToUse == null)
            {
                return processor; // Skip if no validator available
            }

            var logger = _serviceProvider.GetService<ILogger<ValidationDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidationDecorator>.Instance;
            return new ValidationDecorator(processor, validatorToUse, logger);
        });
        return this;
    }

    /// <summary>
    /// Add retry logic to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseRetry(IRetryPolicy? retryPolicy = null)
    {
        _decorators.Add(processor =>
        {
            var logger = _serviceProvider.GetService<ILogger<RetryDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RetryDecorator>.Instance;
            return new RetryDecorator(processor, logger, retryPolicy);
        });
        return this;
    }

    /// <summary>
    /// Add error handling to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseErrorHandling(int maxRetries = 3)
    {
        _decorators.Add(processor =>
        {
            var errorHandler = _serviceProvider.GetService<IErrorHandler>();
            if (errorHandler == null)
            {
                return processor; // Skip if no error handler available
            }

            var logger = _serviceProvider.GetService<ILogger<ErrorHandlingDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ErrorHandlingDecorator>.Instance;
            var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
            return new ErrorHandlingDecorator(processor, errorHandler, logger, timeProvider, maxRetries);
        });
        return this;
    }

    /// <summary>
    /// Add metrics collection to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseMetrics(IMetricsCollector? metricsCollector = null)
    {
        _decorators.Add(processor =>
        {
            var collector = metricsCollector ?? _serviceProvider.GetService<IMetricsCollector>();
            if (collector == null)
            {
                return processor; // Skip if no metrics collector available
            }

            return new MetricsDecorator(processor, collector);
        });
        return this;
    }

    /// <summary>
    /// Add circuit breaker to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseCircuitBreaker(CircuitBreakerOptions? options = null)
    {
        _decorators.Add(processor =>
        {
            var logger = _serviceProvider.GetService<ILogger<CircuitBreakerDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CircuitBreakerDecorator>.Instance;
            var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
            return new CircuitBreakerDecorator(processor, logger, timeProvider, options);
        });
        return this;
    }

    /// <summary>
    /// Add correlation context tracking to the pipeline
    /// Enables choreography pattern by automatically propagating correlation and causation IDs
    /// </summary>
    public MessageProcessingPipelineBuilder UseCorrelation()
    {
        _decorators.Add(processor =>
        {
            var logger = _serviceProvider.GetService<ILogger<CorrelationContextDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CorrelationContextDecorator>.Instance;
            return new CorrelationContextDecorator(processor, logger);
        });
        return this;
    }

    /// <summary>
    /// Add OpenTelemetry instrumentation to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseOpenTelemetry()
    {
        _decorators.Add(processor =>
        {
            // Check if OpenTelemetry decorator type is available
            var openTelemetryDecoratorType = Type.GetType(
                "HeroMessaging.Observability.OpenTelemetry.OpenTelemetryDecorator, HeroMessaging.Observability.OpenTelemetry");

            if (openTelemetryDecoratorType == null)
            {
                // OpenTelemetry package not available, skip this decorator
                return processor;
            }

            // Create instance using reflection
            var decorator = Activator.CreateInstance(openTelemetryDecoratorType, processor);
            return (IMessageProcessor)(decorator ?? processor);
        });
        return this;
    }

    /// <summary>
    /// Add a custom decorator to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder Use(Func<IMessageProcessor, IMessageProcessor> decorator)
    {
        _decorators.Add(decorator);
        return this;
    }

    /// <summary>
    /// Build the pipeline with the configured decorators
    /// </summary>
    public IMessageProcessor Build(IMessageProcessor innerProcessor)
    {
        // Apply decorators in reverse order so they execute in the order they were added
        var pipeline = innerProcessor;
        for (int i = _decorators.Count - 1; i >= 0; i--)
        {
            pipeline = _decorators[i](pipeline);
        }
        return pipeline;
    }
}

/// <summary>
/// Core message processor that wraps a processing function for use in the decorator pipeline.
/// </summary>
/// <param name="processFunc">The core processing function to execute for each message.</param>
/// <remarks>
/// This class adapts a simple processing function into the IMessageProcessor interface,
/// making it suitable for use as the innermost processor in a decorator pipeline.
///
/// The process function receives:
/// - message: The message to process
/// - context: Processing context with metadata
/// - cancellationToken: Cancellation token
///
/// Responsibility:
/// - Executes the actual business logic (handler invocation)
/// - Wraps function in IMessageProcessor interface
/// - Converts exceptions to ProcessingResult.Failed
/// - Returns ProcessingResult.Successful on success
///
/// This is typically used internally by EventBusV2, CommandProcessor, and QueryProcessor
/// to wrap their handler invocation logic in a pipeline-compatible format.
///
/// Usage:
/// <code>
/// var coreProcessor = new CoreMessageProcessor(async (msg, ctx, ct) =>
/// {
///     // Execute handler
///     await handler.Handle(msg, ct);
/// });
///
/// // Wrap with decorators
/// var pipeline = new MessageProcessingPipelineBuilder(serviceProvider)
///     .UseLogging()
///     .UseRetry()
///     .Build(coreProcessor);
/// </code>
/// </remarks>
public class CoreMessageProcessor(Func<IMessage, ProcessingContext, CancellationToken, ValueTask> processFunc) : IMessageProcessor
{
    private readonly Func<IMessage, ProcessingContext, CancellationToken, ValueTask> _processFunc = processFunc;

    /// <summary>
    /// Processes a message by invoking the wrapped processing function.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="context">The processing context with metadata.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A ProcessingResult indicating success or failure.
    /// Returns ProcessingResult.Successful() if the function completes without exception.
    /// Returns ProcessingResult.Failed(exception) if the function throws.
    /// </returns>
    /// <remarks>
    /// This method serves as the innermost layer of the processing pipeline.
    /// All decorators wrap this method, executing their logic before and/or after this call.
    ///
    /// Exception Handling:
    /// - Exceptions from the processing function are caught
    /// - Converted to ProcessingResult.Failed with exception details
    /// - Allows decorators to handle failures appropriately
    /// </remarks>
    public async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _processFunc(message, context, cancellationToken);
            return ProcessingResult.Successful();
        }
        catch (Exception ex)
        {
            return ProcessingResult.Failed(ex);
        }
    }
}