using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Core.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

/// <summary>
/// Builder for creating message processing pipelines with decorators
/// </summary>
public class MessageProcessingPipelineBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<Func<IMessageProcessor, IMessageProcessor>> _decorators = new();
    
    public MessageProcessingPipelineBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    /// <summary>
    /// Add logging to the pipeline
    /// </summary>
    public MessageProcessingPipelineBuilder UseLogging(LogLevel successLevel = LogLevel.Debug, bool logPayload = false)
    {
        _decorators.Add(processor =>
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<LoggingDecorator>>();
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
            
            var logger = _serviceProvider.GetRequiredService<ILogger<ValidationDecorator>>();
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
            var logger = _serviceProvider.GetRequiredService<ILogger<RetryDecorator>>();
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
            
            var logger = _serviceProvider.GetRequiredService<ILogger<ErrorHandlingDecorator>>();
            return new ErrorHandlingDecorator(processor, errorHandler, logger, maxRetries);
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
            var logger = _serviceProvider.GetRequiredService<ILogger<CircuitBreakerDecorator>>();
            return new CircuitBreakerDecorator(processor, logger, options);
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
public class CoreMessageProcessor : IMessageProcessor
{
    private readonly Func<IMessage, ProcessingContext, CancellationToken, ValueTask> _processFunc;
    
    public CoreMessageProcessor(Func<IMessage, ProcessingContext, CancellationToken, ValueTask> processFunc)
    {
        _processFunc = processFunc;
    }
    
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