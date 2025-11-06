using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Idempotency;
using HeroMessaging.Idempotency.Decorators;
using HeroMessaging.Idempotency.KeyGeneration;
using HeroMessaging.Idempotency.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring idempotency support in HeroMessaging.
/// </summary>
public static class ExtensionsToIHeroMessagingBuilderForIdempotency
{
    /// <summary>
    /// Adds idempotency support to the HeroMessaging pipeline.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder.</param>
    /// <param name="configure">Optional action to configure idempotency settings.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method enables exactly-once processing semantics by caching processing results
    /// and returning cached responses for duplicate requests.
    /// </para>
    /// <para>
    /// <strong>Default Configuration</strong>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Storage: InMemoryIdempotencyStore</description></item>
    /// <item><description>Key Generator: MessageIdKeyGenerator</description></item>
    /// <item><description>Success TTL: 24 hours</description></item>
    /// <item><description>Failure TTL: 1 hour</description></item>
    /// <item><description>Cache Failures: Enabled</description></item>
    /// </list>
    /// <para>
    /// <strong>Pipeline Position</strong>: The idempotency decorator is positioned early in the pipeline:
    /// </para>
    /// <list type="number">
    /// <item><description>ValidationDecorator - Validate messages first</description></item>
    /// <item><description>IdempotencyDecorator - Check cache before expensive operations (this)</description></item>
    /// <item><description>RetryDecorator - Avoid retrying cached responses</description></item>
    /// <item><description>CircuitBreakerDecorator - Return cached response even if circuit is open</description></item>
    /// <item><description>Handler Execution</description></item>
    /// </list>
    /// <para>
    /// <strong>Example Usage</strong>:
    /// </para>
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.WithIdempotency(idempotency =>
    ///     {
    ///         idempotency
    ///             .UseInMemoryStore()
    ///             .WithSuccessTtl(TimeSpan.FromDays(7))
    ///             .WithFailureTtl(TimeSpan.FromHours(1))
    ///             .WithFailureCaching(true);
    ///     });
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder WithIdempotency(
        this IHeroMessagingBuilder builder,
        Action<IIdempotencyBuilder>? configure = null)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        var services = builder.Build();
        var idempotencyBuilder = new IdempotencyBuilder(services);

        configure?.Invoke(idempotencyBuilder);

        idempotencyBuilder.Build();

        return builder;
    }
}

/// <summary>
/// Builder interface for configuring idempotency support.
/// </summary>
public interface IIdempotencyBuilder
{
    /// <summary>
    /// Configure the time-to-live for successful response caching.
    /// </summary>
    /// <param name="ttl">The TTL duration. Default is 24 hours.</param>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder WithSuccessTtl(TimeSpan ttl);

    /// <summary>
    /// Configure the time-to-live for failed response caching.
    /// </summary>
    /// <param name="ttl">The TTL duration. Default is 1 hour.</param>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder WithFailureTtl(TimeSpan ttl);

    /// <summary>
    /// Configure whether idempotent failures should be cached.
    /// </summary>
    /// <param name="cacheFailures">
    /// True to cache idempotent failures (default), false to always retry failures.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder WithFailureCaching(bool cacheFailures);

    /// <summary>
    /// Use the default MessageId-based key generator.
    /// </summary>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder UseMessageIdKeyGenerator();

    /// <summary>
    /// Use a custom key generator implementation.
    /// </summary>
    /// <typeparam name="TKeyGenerator">The key generator type.</typeparam>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder UseKeyGenerator<TKeyGenerator>()
        where TKeyGenerator : class, IIdempotencyKeyGenerator;

    /// <summary>
    /// Use in-memory storage for idempotency responses.
    /// </summary>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder UseInMemoryStore();

    /// <summary>
    /// Use a custom storage implementation.
    /// </summary>
    /// <typeparam name="TStore">The storage type.</typeparam>
    /// <returns>The builder for method chaining.</returns>
    IIdempotencyBuilder UseStore<TStore>()
        where TStore : class, IIdempotencyStore;
}

/// <summary>
/// Implementation of the idempotency builder.
/// </summary>
internal sealed class IdempotencyBuilder : IIdempotencyBuilder
{
    private readonly IServiceCollection _services;
    private TimeSpan? _successTtl;
    private TimeSpan? _failureTtl;
    private bool _cacheFailures = true;
    private Type? _keyGeneratorType;
    private Type? _storeType;

    public IdempotencyBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IIdempotencyBuilder WithSuccessTtl(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("Success TTL must be greater than zero.", nameof(ttl));

        _successTtl = ttl;
        return this;
    }

    public IIdempotencyBuilder WithFailureTtl(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("Failure TTL must be greater than zero.", nameof(ttl));

        _failureTtl = ttl;
        return this;
    }

    public IIdempotencyBuilder WithFailureCaching(bool cacheFailures)
    {
        _cacheFailures = cacheFailures;
        return this;
    }

    public IIdempotencyBuilder UseMessageIdKeyGenerator()
    {
        _keyGeneratorType = typeof(MessageIdKeyGenerator);
        return this;
    }

    public IIdempotencyBuilder UseKeyGenerator<TKeyGenerator>()
        where TKeyGenerator : class, IIdempotencyKeyGenerator
    {
        _keyGeneratorType = typeof(TKeyGenerator);
        return this;
    }

    public IIdempotencyBuilder UseInMemoryStore()
    {
        _storeType = typeof(InMemoryIdempotencyStore);
        return this;
    }

    public IIdempotencyBuilder UseStore<TStore>()
        where TStore : class, IIdempotencyStore
    {
        _storeType = typeof(TStore);
        return this;
    }

    internal void Build()
    {
        // Register TimeProvider if not already registered
        _services.TryAddSingleton(TimeProvider.System);

        // Register key generator
        var keyGeneratorType = _keyGeneratorType ?? typeof(MessageIdKeyGenerator);
        _services.TryAddSingleton(typeof(IIdempotencyKeyGenerator), keyGeneratorType);

        // Register storage
        var storeType = _storeType ?? typeof(InMemoryIdempotencyStore);
        _services.TryAddSingleton(typeof(IIdempotencyStore), storeType);

        // Register policy with configured TTLs
        _services.TryAddSingleton<IIdempotencyPolicy>(sp =>
        {
            var keyGenerator = sp.GetRequiredService<IIdempotencyKeyGenerator>();
            return new DefaultIdempotencyPolicy(
                successTtl: _successTtl,
                failureTtl: _failureTtl,
                keyGenerator: keyGenerator,
                cacheFailures: _cacheFailures);
        });

        // Register IdempotencyDecorator factory
        // The decorator will be instantiated per message processor
        _services.TryAddSingleton<Func<IMessageProcessor, IdempotencyDecorator>>(sp =>
        {
            var store = sp.GetRequiredService<IIdempotencyStore>();
            var policy = sp.GetRequiredService<IIdempotencyPolicy>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

            return inner =>
            {
                var logger = loggerFactory.CreateLogger<IdempotencyDecorator>();
                return new IdempotencyDecorator(inner, store, policy, logger, timeProvider);
            };
        });
    }
}
