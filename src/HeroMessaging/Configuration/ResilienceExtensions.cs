using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Resilience;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring connection resilience in HeroMessaging
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>
    /// Adds connection resilience to all storage operations
    /// Includes retry logic and circuit breaker patterns for database operations
    /// </summary>
    public static IHeroMessagingBuilder WithConnectionResilience(
        this IHeroMessagingBuilder builder,
        ConnectionResilienceOptions? options = null)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;
        options ??= new ConnectionResilienceOptions();

        // Register resilience policy
        services.TryAddSingleton<IConnectionResiliencePolicy>(serviceProvider =>
            new DefaultConnectionResiliencePolicy(
                options,
                serviceProvider.GetRequiredService<ILogger<DefaultConnectionResiliencePolicy>>(),
                serviceProvider.GetRequiredService<TimeProvider>()));

        // Decorate UnitOfWorkFactory with resilience
        services.Decorate<IUnitOfWorkFactory>((inner, serviceProvider) =>
            new ResilientUnitOfWorkFactory(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>(),
                serviceProvider.GetRequiredService<ILogger<ResilientUnitOfWorkFactory>>()));

        // Decorate storage implementations with resilience
        services.Decorate<IMessageStorage>((inner, serviceProvider) =>
            new ResilientMessageStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IOutboxStorage>((inner, serviceProvider) =>
            new ResilientOutboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IInboxStorage>((inner, serviceProvider) =>
            new ResilientInboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IQueueStorage>((inner, serviceProvider) =>
            new ResilientQueueStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        return builder;
    }

    /// <summary>
    /// Adds connection resilience with custom configuration
    /// </summary>
    public static IHeroMessagingBuilder WithConnectionResilience(
        this IHeroMessagingBuilder builder,
        Action<ConnectionResilienceOptions> configure)
    {
        var options = new ConnectionResilienceOptions();
        configure(options);
        return builder.WithConnectionResilience(options);
    }

    /// <summary>
    /// Adds aggressive connection resilience for high-availability scenarios
    /// </summary>
    public static IHeroMessagingBuilder WithHighAvailabilityResilience(
        this IHeroMessagingBuilder builder)
    {
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 5,
            BaseRetryDelay = TimeSpan.FromMilliseconds(500),
            MaxRetryDelay = TimeSpan.FromSeconds(60),
            CircuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 10,
                BreakDuration = TimeSpan.FromMinutes(2)
            }
        };

        return builder.WithConnectionResilience(options);
    }

    /// <summary>
    /// Adds conservative connection resilience for development scenarios
    /// </summary>
    public static IHeroMessagingBuilder WithDevelopmentResilience(
        this IHeroMessagingBuilder builder)
    {
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 2,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromSeconds(10),
            CircuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromSeconds(15)
            }
        };

        return builder.WithConnectionResilience(options);
    }

    /// <summary>
    /// Adds connection resilience only to write operations (outbox, inbox, commands)
    /// Useful when read operations can tolerate failures better than writes
    /// </summary>
    public static IHeroMessagingBuilder WithWriteOnlyResilience(
        this IHeroMessagingBuilder builder,
        ConnectionResilienceOptions? options = null)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;
        options ??= new ConnectionResilienceOptions();

        // Register resilience policy
        services.TryAddSingleton<IConnectionResiliencePolicy>(serviceProvider =>
            new DefaultConnectionResiliencePolicy(
                options,
                serviceProvider.GetRequiredService<ILogger<DefaultConnectionResiliencePolicy>>(),
                serviceProvider.GetRequiredService<TimeProvider>()));

        // Decorate UnitOfWorkFactory with resilience (for transactional operations)
        services.Decorate<IUnitOfWorkFactory>((inner, serviceProvider) =>
            new ResilientUnitOfWorkFactory(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>(),
                serviceProvider.GetRequiredService<ILogger<ResilientUnitOfWorkFactory>>()));

        // Only decorate write-oriented storage
        services.Decorate<IOutboxStorage>((inner, serviceProvider) =>
            new ResilientOutboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IInboxStorage>((inner, serviceProvider) =>
            new ResilientInboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IQueueStorage>((inner, serviceProvider) =>
            new ResilientQueueStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        return builder;
    }

    /// <summary>
    /// Adds custom connection resilience policy
    /// </summary>
    public static IHeroMessagingBuilder WithConnectionResilience<TPolicy>(
        this IHeroMessagingBuilder builder)
        where TPolicy : class, IConnectionResiliencePolicy
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;

        // Register custom resilience policy
        services.TryAddSingleton<IConnectionResiliencePolicy, TPolicy>();

        // Apply decorators using the custom policy
        services.Decorate<IUnitOfWorkFactory>((inner, serviceProvider) =>
            new ResilientUnitOfWorkFactory(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>(),
                serviceProvider.GetRequiredService<ILogger<ResilientUnitOfWorkFactory>>()));

        services.Decorate<IMessageStorage>((inner, serviceProvider) =>
            new ResilientMessageStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IOutboxStorage>((inner, serviceProvider) =>
            new ResilientOutboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IInboxStorage>((inner, serviceProvider) =>
            new ResilientInboxStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        services.Decorate<IQueueStorage>((inner, serviceProvider) =>
            new ResilientQueueStorageDecorator(
                inner,
                serviceProvider.GetRequiredService<IConnectionResiliencePolicy>()));

        return builder;
    }
}

/// <summary>
/// Predefined resilience configuration profiles
/// </summary>
public static class ResilienceProfiles
{
    /// <summary>
    /// Configuration optimized for cloud environments with higher latency tolerance
    /// </summary>
    public static ConnectionResilienceOptions Cloud => new()
    {
        MaxRetries = 5,
        BaseRetryDelay = TimeSpan.FromSeconds(2),
        MaxRetryDelay = TimeSpan.FromMinutes(2),
        CircuitBreakerOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 8,
            BreakDuration = TimeSpan.FromMinutes(3)
        }
    };

    /// <summary>
    /// Configuration optimized for on-premises environments with fast networks
    /// </summary>
    public static ConnectionResilienceOptions OnPremises => new()
    {
        MaxRetries = 3,
        BaseRetryDelay = TimeSpan.FromMilliseconds(500),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        CircuitBreakerOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            BreakDuration = TimeSpan.FromMinutes(1)
        }
    };

    /// <summary>
    /// Configuration for microservices with strict SLA requirements
    /// </summary>
    public static ConnectionResilienceOptions Microservices => new()
    {
        MaxRetries = 4,
        BaseRetryDelay = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(45),
        CircuitBreakerOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 6,
            BreakDuration = TimeSpan.FromMinutes(1.5)
        }
    };

    /// <summary>
    /// Configuration for batch processing scenarios with higher tolerance for delays
    /// </summary>
    public static ConnectionResilienceOptions BatchProcessing => new()
    {
        MaxRetries = 7,
        BaseRetryDelay = TimeSpan.FromSeconds(3),
        MaxRetryDelay = TimeSpan.FromMinutes(5),
        CircuitBreakerOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 12,
            BreakDuration = TimeSpan.FromMinutes(5)
        }
    };
}