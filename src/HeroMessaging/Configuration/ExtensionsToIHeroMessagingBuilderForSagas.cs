using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Extension methods for configuring saga orchestration in HeroMessaging
/// </summary>
public static class ExtensionsToIHeroMessagingBuilderForSagas
{
    /// <summary>
    /// Add saga orchestration support to HeroMessaging
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Optional saga configuration action</param>
    /// <returns>The builder for method chaining</returns>
    public static IHeroMessagingBuilder WithSagaOrchestration(
        this IHeroMessagingBuilder builder,
        Action<ISagaBuilder>? configure = null)
    {
        var services = builder.Build();
        var sagaBuilder = new SagaBuilder(services);

        configure?.Invoke(sagaBuilder);

        return builder;
    }

    /// <summary>
    /// Register a saga with its state machine definition
    /// </summary>
    /// <typeparam name="TSaga">The saga type</typeparam>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="stateMachineFactory">Factory to create the state machine definition</param>
    /// <returns>The builder for method chaining</returns>
    public static IHeroMessagingBuilder AddSaga<TSaga>(
        this IHeroMessagingBuilder builder,
        Func<StateMachineDefinition<TSaga>> stateMachineFactory)
        where TSaga : class, ISaga, new()
    {
        var services = builder.Build();

        // Register TimeProvider.System if not already registered
        services.TryAddSingleton(TimeProvider.System);

        // Register state machine definition as singleton
        services.AddSingleton(stateMachineFactory());

        // Register saga orchestrator
        services.TryAddScoped<SagaOrchestrator<TSaga>>();

        return builder;
    }

    /// <summary>
    /// Register a saga with a state machine builder configuration
    /// </summary>
    /// <typeparam name="TSaga">The saga type</typeparam>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Action to configure the state machine</param>
    /// <returns>The builder for method chaining</returns>
    public static IHeroMessagingBuilder AddSaga<TSaga>(
        this IHeroMessagingBuilder builder,
        Action<StateMachineBuilder<TSaga>> configure)
        where TSaga : class, ISaga, new()
    {
        var stateMachineBuilder = new StateMachineBuilder<TSaga>();
        configure(stateMachineBuilder);
        var stateMachine = stateMachineBuilder.Build();

        return builder.AddSaga(() => stateMachine);
    }

    /// <summary>
    /// Use in-memory saga repository (for development/testing)
    /// </summary>
    /// <typeparam name="TSaga">The saga type</typeparam>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <returns>The builder for method chaining</returns>
    public static IHeroMessagingBuilder UseInMemorySagaRepository<TSaga>(
        this IHeroMessagingBuilder builder)
        where TSaga : class, ISaga
    {
        var services = builder.Build();
        // Register TimeProvider.System if not already registered
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISagaRepository<TSaga>, InMemorySagaRepository<TSaga>>();
        return builder;
    }

    /// <summary>
    /// Use a custom saga repository implementation
    /// </summary>
    /// <typeparam name="TSaga">The saga type</typeparam>
    /// <typeparam name="TRepository">The repository implementation type</typeparam>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <returns>The builder for method chaining</returns>
    public static IHeroMessagingBuilder UseSagaRepository<TSaga, TRepository>(
        this IHeroMessagingBuilder builder)
        where TSaga : class, ISaga
        where TRepository : class, ISagaRepository<TSaga>
    {
        var services = builder.Build();
        services.TryAddScoped<ISagaRepository<TSaga>, TRepository>();
        return builder;
    }
}

/// <summary>
/// Builder interface for configuring saga orchestration
/// </summary>
public interface ISagaBuilder
{
    /// <summary>
    /// Add a saga with its state machine definition
    /// </summary>
    ISagaBuilder AddSaga<TSaga>(Func<StateMachineDefinition<TSaga>> stateMachineFactory)
        where TSaga : class, ISaga, new();

    /// <summary>
    /// Add a saga with state machine configuration
    /// </summary>
    ISagaBuilder AddSaga<TSaga>(Action<StateMachineBuilder<TSaga>> configure)
        where TSaga : class, ISaga, new();

    /// <summary>
    /// Use in-memory repository for all sagas
    /// </summary>
    ISagaBuilder UseInMemoryRepositories();

    /// <summary>
    /// Configure saga timeout handling (global settings)
    /// </summary>
    ISagaBuilder WithTimeoutHandling(TimeSpan checkInterval, TimeSpan defaultTimeout);

    /// <summary>
    /// Enable timeout handling for a specific saga type
    /// </summary>
    ISagaBuilder WithTimeoutHandling<TSaga>(TimeSpan checkInterval, TimeSpan defaultTimeout)
        where TSaga : class, ISaga;
}

/// <summary>
/// Implementation of saga builder
/// </summary>
internal class SagaBuilder : ISagaBuilder
{
    private readonly IServiceCollection _services;

    public SagaBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ISagaBuilder AddSaga<TSaga>(Func<StateMachineDefinition<TSaga>> stateMachineFactory)
        where TSaga : class, ISaga, new()
    {
        _services.AddSingleton(stateMachineFactory());
        _services.TryAddScoped<SagaOrchestrator<TSaga>>();
        _services.TryAddSingleton<ISagaRepository<TSaga>, InMemorySagaRepository<TSaga>>();
        return this;
    }

    public ISagaBuilder AddSaga<TSaga>(Action<StateMachineBuilder<TSaga>> configure)
        where TSaga : class, ISaga, new()
    {
        var builder = new StateMachineBuilder<TSaga>();
        configure(builder);
        var stateMachine = builder.Build();

        return AddSaga(() => stateMachine);
    }

    public ISagaBuilder UseInMemoryRepositories()
    {
        // In-memory repositories are registered by default in AddSaga
        // This method is here for explicit configuration
        return this;
    }

    public ISagaBuilder WithTimeoutHandling(TimeSpan checkInterval, TimeSpan defaultTimeout)
    {
        // Register saga timeout handler options
        _services.AddSingleton(new SagaTimeoutOptions
        {
            CheckInterval = checkInterval,
            DefaultTimeout = defaultTimeout,
            Enabled = true
        });

        return this;
    }

    /// <summary>
    /// Enable timeout handling for a specific saga type
    /// </summary>
    public ISagaBuilder WithTimeoutHandling<TSaga>(TimeSpan checkInterval, TimeSpan defaultTimeout)
        where TSaga : class, ISaga
    {
        // Register saga-specific timeout options
        _services.AddSingleton(new SagaTimeoutOptions
        {
            CheckInterval = checkInterval,
            DefaultTimeout = defaultTimeout,
            Enabled = true
        });

        // Register the timeout handler as a hosted service for this saga type
        _services.AddHostedService<SagaTimeoutHandler<TSaga>>();

        return this;
    }
}
