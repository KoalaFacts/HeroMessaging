using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>
/// Pre-configured pipeline configurations for different scenarios
/// </summary>
public static class PipelineConfigurations
{
    /// <summary>
    /// Pipeline for high-throughput scenarios with minimal overhead
    /// Optimized for maximum message processing speed with reduced reliability features
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>Configured pipeline builder with metrics and minimal retry logic</returns>
    /// <remarks>
    /// This configuration prioritizes throughput over reliability:
    /// - Metrics collection enabled for monitoring
    /// - Single retry attempt with exponential backoff
    /// - No validation, logging, or circuit breaker overhead
    /// Recommended for non-critical messages where performance is paramount
    /// </remarks>
    public static MessageProcessingPipelineBuilder HighThroughput(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseMetrics()
            .UseRetry(new ExponentialBackoffRetryPolicy(maxRetries: 1)); // Minimal retries
    }

    /// <summary>
    /// Pipeline for critical business operations with full safety
    /// Includes comprehensive error handling, validation, and resilience features
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>Configured pipeline builder with all safety and reliability features enabled</returns>
    /// <remarks>
    /// This configuration prioritizes reliability and correctness:
    /// - Full metrics and payload logging enabled
    /// - Message validation before processing
    /// - Circuit breaker (30% failure rate threshold, 1-minute break)
    /// - Error handling with 5 retry attempts
    /// - Exponential backoff retry policy (up to 5 retries)
    /// Recommended for financial transactions, order processing, and other critical operations
    /// </remarks>
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
    /// Optimized for debugging and troubleshooting during development
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>Configured pipeline builder with debug logging and validation</returns>
    /// <remarks>
    /// This configuration helps developers identify issues:
    /// - Debug-level logging with full payload visibility
    /// - Message validation to catch schema issues early
    /// - Moderate retry logic (2 attempts)
    /// - No metrics overhead or circuit breaker complexity
    /// Not recommended for production use due to verbose logging
    /// </remarks>
    public static MessageProcessingPipelineBuilder Development(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider)
            .UseLogging(LogLevel.Debug, logPayload: true)
            .UseValidation()
            .UseRetry(new ExponentialBackoffRetryPolicy(maxRetries: 2));
    }

    /// <summary>
    /// Pipeline for integration scenarios with external systems
    /// Balanced configuration for reliability when communicating with third-party services
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>Configured pipeline builder with resilience features for external integrations</returns>
    /// <remarks>
    /// This configuration balances reliability with external system constraints:
    /// - Metrics and information-level logging enabled
    /// - Message validation to catch integration issues
    /// - Circuit breaker (50% failure rate, 30-second break) for external service protection
    /// - Error handling with 3 retry attempts
    /// - Exponential backoff (2s base, up to 1 minute max delay)
    /// Recommended for API integrations, webhook handlers, and third-party messaging
    /// </remarks>
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
    /// Bare-bones configuration for maximum performance or custom decorator composition
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>Empty pipeline builder ready for custom decorator configuration</returns>
    /// <remarks>
    /// This configuration provides a clean slate:
    /// - No logging, metrics, validation, or error handling
    /// - Lowest possible overhead
    /// - Useful for custom pipeline composition or benchmarking
    /// Only recommended when you need full control over the decorator chain
    /// </remarks>
    public static MessageProcessingPipelineBuilder Minimal(IServiceProvider serviceProvider)
    {
        return new MessageProcessingPipelineBuilder(serviceProvider);
    }
}

/// <summary>
/// Extension methods for pipeline configuration
/// Provides fluent API for registering and configuring message processing pipelines
/// </summary>
public static class PipelineExtensions
{
    /// <summary>
    /// Registers message processing pipeline services with the dependency injection container
    /// </summary>
    /// <param name="services">Service collection to register pipeline services into</param>
    /// <returns>Service collection for method chaining</returns>
    /// <remarks>
    /// Registers the following services:
    /// - IMetricsCollector as InMemoryMetricsCollector (singleton)
    /// - MessageProcessingPipelineBuilder factory (transient)
    /// Call this method during application startup before configuring specific pipelines
    /// </remarks>
    public static IServiceCollection AddMessageProcessingPipeline(this IServiceCollection services)
    {
        // Register default services for pipelines
        services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();

        // Register pipeline builder as factory
        services.AddTransient<MessageProcessingPipelineBuilder>(provider =>
            new MessageProcessingPipelineBuilder(provider));

        return services;
    }

    /// <summary>
    /// Applies a predefined pipeline configuration based on the specified profile
    /// </summary>
    /// <param name="builder">Pipeline builder to configure (not used, replaced by profile configuration)</param>
    /// <param name="profile">Profile type defining the pipeline configuration to apply</param>
    /// <param name="serviceProvider">Service provider for resolving pipeline dependencies</param>
    /// <returns>New pipeline builder configured according to the selected profile</returns>
    /// <remarks>
    /// This method replaces the current builder with a new one configured for the profile.
    /// Available profiles:
    /// - HighThroughput: Maximum performance, minimal overhead
    /// - CriticalBusiness: Maximum reliability with all safety features
    /// - Development: Debug logging and validation for troubleshooting
    /// - Integration: Balanced resilience for external system communication
    /// - Minimal: Empty pipeline for custom configuration
    /// </remarks>
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

/// <summary>
/// Predefined pipeline configuration profiles for common messaging scenarios
/// </summary>
public enum PipelineProfile
{
    /// <summary>
    /// High-throughput configuration optimized for maximum message processing speed
    /// Includes metrics and minimal retry logic, no validation or circuit breaker
    /// </summary>
    HighThroughput,

    /// <summary>
    /// Critical business configuration with comprehensive error handling and reliability features
    /// Includes metrics, logging, validation, circuit breaker, and aggressive retry policies
    /// </summary>
    CriticalBusiness,

    /// <summary>
    /// Development configuration with extensive debug logging and validation
    /// Optimized for troubleshooting and identifying integration issues
    /// </summary>
    Development,

    /// <summary>
    /// Integration configuration balanced for external system communication
    /// Includes resilience features appropriate for third-party API interactions
    /// </summary>
    Integration,

    /// <summary>
    /// Minimal configuration with no decorators
    /// Provides bare pipeline for custom decorator composition
    /// </summary>
    Minimal
}