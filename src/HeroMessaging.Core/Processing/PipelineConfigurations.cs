using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Core.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing;

/// <summary>
/// Pre-configured pipeline configurations for different scenarios
/// </summary>
public static class PipelineConfigurations
{
    /// <summary>
    /// Pipeline for high-throughput scenarios with minimal overhead
    /// </summary>
    public static MessageProcessingPipelineBuilder HighThroughput(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseMetrics()
            .UseRetry(new ExponentialBackoffRetryPolicy(maxRetries: 1)); // Minimal retries
    }
    
    /// <summary>
    /// Pipeline for critical business operations with full safety
    /// </summary>
    public static MessageProcessingPipelineBuilder CriticalBusiness(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseMetrics()
            .UseLogging(LogLevel.Information, logPayload: true)
            .UseValidation()
            .UseCircuitBreaker(new CircuitBreakerOptions
            {
                FailureThreshold = 10,
                FailureRateThreshold = 0.3,
                BreakDuration = TimeSpan.FromMinutes(1),
                MinimumThroughput = 20
            })
            .UseErrorHandling(maxRetries: 5)
            .UseRetry(new ExponentialBackoffRetryPolicy(maxRetries: 5));
    }
    
    /// <summary>
    /// Pipeline for development with extensive logging
    /// </summary>
    public static MessageProcessingPipelineBuilder Development(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseLogging(LogLevel.Debug, logPayload: true)
            .UseValidation()
            .UseRetry(new ExponentialBackoffRetryPolicy(maxRetries: 2));
    }
    
    /// <summary>
    /// Pipeline for integration scenarios with external systems
    /// </summary>
    public static MessageProcessingPipelineBuilder Integration(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseMetrics()
            .UseLogging(LogLevel.Information)
            .UseValidation()
            .UseCircuitBreaker(new CircuitBreakerOptions
            {
                FailureThreshold = 5,
                FailureRateThreshold = 0.5,
                BreakDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10
            })
            .UseErrorHandling(maxRetries: 3)
            .UseRetry(new ExponentialBackoffRetryPolicy(
                maxRetries: 3,
                baseDelay: TimeSpan.FromSeconds(2),
                maxDelay: TimeSpan.FromMinutes(1)));
    }
    
    /// <summary>
    /// Minimal pipeline with no decorators
    /// </summary>
    public static MessageProcessingPipelineBuilder Minimal(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider);
    }
}

/// <summary>
/// Extension methods for pipeline configuration
/// </summary>
public static class PipelineExtensions
{
    public static IServiceCollection AddMessageProcessingPipeline(this IServiceCollection services)
    {
        // Register default services for pipelines
        services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
        
        // Register pipeline builder as factory
        services.AddTransient<MessageProcessingPipelineBuilder>(provider => 
            new MessageProcessingPipelineBuilder(provider));
        
        return services;
    }
    
    public static MessageProcessingPipelineBuilder UsePredefinedPipeline(
        this MessageProcessingPipelineBuilder builder,
        PipelineProfile profile,
        IServiceProvider serviceProvider)
    {
        return profile switch
        {
            PipelineProfile.HighThroughput => PipelineConfigurations.HighThroughput(serviceProvider),
            PipelineProfile.CriticalBusiness => PipelineConfigurations.CriticalBusiness(serviceProvider),
            PipelineProfile.Development => PipelineConfigurations.Development(serviceProvider),
            PipelineProfile.Integration => PipelineConfigurations.Integration(serviceProvider),
            PipelineProfile.Minimal => PipelineConfigurations.Minimal(serviceProvider),
            _ => builder
        };
    }
}

public enum PipelineProfile
{
    HighThroughput,
    CriticalBusiness,
    Development,
    Integration,
    Minimal
}