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
/// Builder interface for configuring saga orchestration in HeroMessaging.
/// </summary>
/// <remarks>
/// This builder provides methods to configure saga orchestration including:
/// - Registering saga types with their state machine definitions
/// - Configuring saga repositories (in-memory or custom)
/// - Setting up timeout handling for long-running sagas
/// - Configuring state machine transitions and behaviors
///
/// Sagas are long-running business processes that coordinate multiple steps
/// and maintain state across asynchronous operations. They're ideal for:
/// - Multi-step workflows (order processing, user onboarding)
/// - Compensating transactions in distributed systems
/// - Complex business processes with conditional branching
///
/// The builder is obtained through the WithSagaOrchestration() extension method
/// on IHeroMessagingBuilder.
///
/// Example usage:
/// <code>
/// builder.WithSagaOrchestration(sagas =>
/// {
///     sagas.AddSaga&lt;OrderSaga&gt;(sm =>
///     {
///         sm.Initially(s => s.OrderCreated, a => a.TransitionTo(OrderState.PaymentPending));
///         sm.During(OrderState.PaymentPending)
///           .When(s => s.PaymentReceived, a => a.TransitionTo(OrderState.Processing));
///     });
///     sagas.UseInMemoryRepositories();
///     sagas.WithTimeoutHandling&lt;OrderSaga&gt;(TimeSpan.FromSeconds(30), TimeSpan.FromHours(24));
/// });
/// </code>
/// </remarks>
public interface ISagaBuilder
{
    /// <summary>
    /// Registers a saga with a pre-built state machine definition.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to register. Must implement ISaga and have a parameterless constructor.</typeparam>
    /// <param name="stateMachineFactory">Factory function that creates the state machine definition</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method registers a saga using a factory function that provides a complete
    /// StateMachineDefinition. Use this when you have complex state machine configuration
    /// logic or want to reuse state machine definitions.
    ///
    /// The saga orchestrator and default in-memory repository are automatically registered.
    ///
    /// Example:
    /// <code>
    /// sagas.AddSaga&lt;OrderSaga&gt;(() =>
    /// {
    ///     var sm = new StateMachineDefinition&lt;OrderSaga&gt;();
    ///     sm.Initially(s => s.OrderCreated, a => a.TransitionTo(OrderState.PaymentPending));
    ///     sm.During(OrderState.PaymentPending)
    ///       .When(s => s.PaymentReceived, a => a.TransitionTo(OrderState.Processing));
    ///     return sm;
    /// });
    /// </code>
    /// </remarks>
    ISagaBuilder AddSaga<TSaga>(Func<StateMachineDefinition<TSaga>> stateMachineFactory)
        where TSaga : class, ISaga, new();

    /// <summary>
    /// Registers a saga with inline state machine configuration.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to register. Must implement ISaga and have a parameterless constructor.</typeparam>
    /// <param name="configure">Action to configure the state machine using the builder pattern</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method registers a saga using a fluent configuration action. This is the
    /// recommended approach for most scenarios as it provides a clean, readable syntax
    /// for defining state transitions.
    ///
    /// The state machine builder supports:
    /// - Initial state configuration with Initially()
    /// - State-specific transitions with During()
    /// - Event-driven transitions with When()
    /// - Conditional actions with If() and Else()
    /// - Compensating actions for failures
    ///
    /// Example:
    /// <code>
    /// sagas.AddSaga&lt;OrderSaga&gt;(sm =>
    /// {
    ///     sm.Initially(s => s.OrderCreated, a => a.TransitionTo(OrderState.PaymentPending));
    ///
    ///     sm.During(OrderState.PaymentPending)
    ///       .When(s => s.PaymentReceived, a => a.TransitionTo(OrderState.Processing))
    ///       .When(s => s.PaymentFailed, a => a.TransitionTo(OrderState.Cancelled));
    ///
    ///     sm.During(OrderState.Processing)
    ///       .When(s => s.OrderShipped, a => a.TransitionTo(OrderState.Completed));
    /// });
    /// </code>
    /// </remarks>
    ISagaBuilder AddSaga<TSaga>(Action<StateMachineBuilder<TSaga>> configure)
        where TSaga : class, ISaga, new();

    /// <summary>
    /// Configures all sagas to use in-memory repositories for state persistence.
    /// </summary>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// In-memory repositories are ideal for:
    /// - Local development and testing
    /// - Prototyping saga workflows
    /// - CI/CD environments without database dependencies
    /// - Short-lived sagas that don't require durability
    ///
    /// Warning: All saga state is lost when the application stops. For production
    /// scenarios with long-running sagas, use a durable repository implementation
    /// (SQL Server, PostgreSQL, etc.).
    ///
    /// Note: In-memory repositories are registered by default when adding sagas.
    /// This method is provided for explicit configuration and clarity.
    ///
    /// Example:
    /// <code>
    /// sagas.UseInMemoryRepositories();
    /// </code>
    /// </remarks>
    ISagaBuilder UseInMemoryRepositories();

    /// <summary>
    /// Configures global timeout handling for all sagas.
    /// </summary>
    /// <param name="checkInterval">How often to check for timed-out sagas</param>
    /// <param name="defaultTimeout">Default timeout duration for sagas that don't specify their own timeout</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Timeout handling automatically detects and processes sagas that have exceeded
    /// their timeout duration without completing. This is essential for:
    /// - Preventing stuck sagas from blocking resources
    /// - Implementing time-based business rules
    /// - Triggering compensating actions for abandoned workflows
    /// - Cleaning up stale saga instances
    ///
    /// When a saga times out, the timeout handler can:
    /// - Transition the saga to a timeout state
    /// - Execute compensating transactions
    /// - Send notification events
    /// - Clean up allocated resources
    ///
    /// Example:
    /// <code>
    /// sagas.WithTimeoutHandling(
    ///     checkInterval: TimeSpan.FromSeconds(30),
    ///     defaultTimeout: TimeSpan.FromHours(24)
    /// );
    /// </code>
    ///
    /// Note: Individual sagas can override the default timeout by setting a timeout
    /// value in their state machine definition.
    /// </remarks>
    ISagaBuilder WithTimeoutHandling(TimeSpan checkInterval, TimeSpan defaultTimeout);

    /// <summary>
    /// Configures timeout handling for a specific saga type.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to configure timeout handling for. Must implement ISaga.</typeparam>
    /// <param name="checkInterval">How often to check for timed-out instances of this saga type</param>
    /// <param name="defaultTimeout">Default timeout duration for instances of this saga type</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method configures saga-specific timeout handling, allowing different timeout
    /// policies for different saga types. This is useful when:
    /// - Different business processes have different SLA requirements
    /// - Some sagas are time-critical while others are long-running
    /// - You need fine-grained control over timeout behavior
    ///
    /// The timeout handler is registered as a hosted service that runs in the background
    /// and periodically checks for timed-out saga instances of the specified type.
    ///
    /// Example:
    /// <code>
    /// // Fast timeout for payment processing
    /// sagas.WithTimeoutHandling&lt;PaymentSaga&gt;(
    ///     checkInterval: TimeSpan.FromSeconds(10),
    ///     defaultTimeout: TimeSpan.FromMinutes(5)
    /// );
    ///
    /// // Longer timeout for order fulfillment
    /// sagas.WithTimeoutHandling&lt;OrderFulfillmentSaga&gt;(
    ///     checkInterval: TimeSpan.FromMinutes(5),
    ///     defaultTimeout: TimeSpan.FromDays(7)
    /// );
    /// </code>
    ///
    /// Note: This configuration takes precedence over global timeout settings for the
    /// specified saga type.
    /// </remarks>
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
