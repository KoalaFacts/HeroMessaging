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
/// Builder for creating message processing pipelines with decorators
/// </summary>
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
            var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
            return new LoggingDecorator(processor, logger, timeProvider, successLevel, logPayload);
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
            var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
            return new RetryDecorator(processor, logger, timeProvider, retryPolicy);
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

            var timeProvider = _serviceProvider.GetRequiredService<TimeProvider>();
            return new MetricsDecorator(processor, collector, timeProvider);
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
    /// Add rate limiting to the pipeline
    /// Controls the rate at which messages are processed to protect downstream systems
    /// </summary>
    /// <param name="rateLimiter">Optional rate limiter instance. If not provided, attempts to resolve from service provider.</param>
    /// <returns>The builder for method chaining.</returns>
    public MessageProcessingPipelineBuilder UseRateLimiting(IRateLimiter? rateLimiter = null)
    {
        _decorators.Add(processor =>
        {
            var limiter = rateLimiter ?? _serviceProvider.GetService<IRateLimiter>();
            if (limiter == null)
            {
                return processor; // Skip if no rate limiter available
            }

            var logger = _serviceProvider.GetService<ILogger<RateLimitingDecorator>>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RateLimitingDecorator>.Instance;
            return new RateLimitingDecorator(processor, limiter, logger);
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
/// Core message processor that does the actual work
/// </summary>
public class CoreMessageProcessor(Func<IMessage, ProcessingContext, CancellationToken, ValueTask> processFunc) : IMessageProcessor
{
    private readonly Func<IMessage, ProcessingContext, CancellationToken, ValueTask> _processFunc = processFunc;


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