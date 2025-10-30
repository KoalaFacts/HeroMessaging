using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Builder for configuring storage providers and storage-related options in HeroMessaging.
/// </summary>
/// <remarks>
/// This builder provides a fluent API for configuring:
/// - Storage providers (in-memory, SQL Server, PostgreSQL, custom)
/// - Connection pooling
/// - Retry policies
/// - Circuit breakers
/// - Command timeouts
///
/// Storage configuration is critical for production deployments and affects
/// message durability, performance, and reliability.
///
/// Example:
/// <code>
/// var storageBuilder = new StorageBuilder(services);
/// storageBuilder
///     .UseInMemory()
///     .WithConnectionPooling(maxPoolSize: 100)
///     .WithRetry(maxRetries: 3)
///     .WithCircuitBreaker(failureThreshold: 5)
///     .Build();
/// </code>
/// </remarks>
public class StorageBuilder : IStorageBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the StorageBuilder class.
    /// </summary>
    /// <param name="services">The service collection to register storage services with</param>
    /// <exception cref="ArgumentNullException">Thrown when services is null</exception>
    public StorageBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Configures HeroMessaging to use in-memory storage for all storage operations.
    /// </summary>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// In-memory storage is non-durable and suitable only for development and testing.
    /// All messages are lost when the application stops.
    ///
    /// This registers in-memory implementations for all storage interfaces:
    /// - IMessageStorage
    /// - IOutboxStorage
    /// - IInboxStorage
    /// - IQueueStorage
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseInMemory();
    /// </code>
    /// </remarks>
    public IStorageBuilder UseInMemory()
    {
        _services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        _services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        _services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        _services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();


        return this;
    }

    /// <summary>
    /// Configures HeroMessaging to use in-memory storage with custom options.
    /// </summary>
    /// <param name="configure">Configuration action for in-memory storage options</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this overload to configure in-memory storage behavior such as:
    /// - Maximum message capacity
    /// - Retention policies
    /// - Eviction strategies
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseInMemory(options =>
    /// {
    ///     options.MaxMessages = 10000;
    ///     options.EvictionPolicy = EvictionPolicy.LeastRecentlyUsed;
    /// });
    /// </code>
    /// </remarks>
    public IStorageBuilder UseInMemory(Action<InMemoryStorageOptions> configure)
    {
        var options = new InMemoryStorageOptions();
        configure(options);

        _services.AddSingleton(options);
        return UseInMemory();
    }

    /// <summary>
    /// Registers a custom IMessageStorage implementation.
    /// </summary>
    /// <typeparam name="T">The custom message storage implementation type</typeparam>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a custom message storage implementation.
    /// The storage will be resolved from the DI container.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseMessageStorage&lt;RedisMessageStorage&gt;();
    /// </code>
    /// </remarks>
    public IStorageBuilder UseMessageStorage<T>() where T : class, IMessageStorage
    {
        _services.AddSingleton<IMessageStorage, T>();
        return this;
    }

    /// <summary>
    /// Registers a specific IMessageStorage instance.
    /// </summary>
    /// <param name="storage">The message storage instance to use</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a pre-configured message storage instance.
    ///
    /// Example:
    /// <code>
    /// var storage = new CustomMessageStorage(connectionString);
    /// storageBuilder.UseMessageStorage(storage);
    /// </code>
    /// </remarks>
    public IStorageBuilder UseMessageStorage(IMessageStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    /// <summary>
    /// Registers a custom IOutboxStorage implementation for the Outbox pattern.
    /// </summary>
    /// <typeparam name="T">The custom outbox storage implementation type</typeparam>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a custom outbox storage implementation.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseOutboxStorage&lt;SqlServerOutboxStorage&gt;();
    /// </code>
    /// </remarks>
    public IStorageBuilder UseOutboxStorage<T>() where T : class, IOutboxStorage
    {
        _services.AddSingleton<IOutboxStorage, T>();
        return this;
    }

    /// <summary>
    /// Registers a specific IOutboxStorage instance for the Outbox pattern.
    /// </summary>
    /// <param name="storage">The outbox storage instance to use</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a pre-configured outbox storage instance.
    ///
    /// Example:
    /// <code>
    /// var storage = new CustomOutboxStorage(connectionString);
    /// storageBuilder.UseOutboxStorage(storage);
    /// </code>
    /// </remarks>
    public IStorageBuilder UseOutboxStorage(IOutboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    /// <summary>
    /// Registers a custom IInboxStorage implementation for the Inbox pattern.
    /// </summary>
    /// <typeparam name="T">The custom inbox storage implementation type</typeparam>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a custom inbox storage implementation.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseInboxStorage&lt;PostgreSqlInboxStorage&gt;();
    /// </code>
    /// </remarks>
    public IStorageBuilder UseInboxStorage<T>() where T : class, IInboxStorage
    {
        _services.AddSingleton<IInboxStorage, T>();
        return this;
    }

    /// <summary>
    /// Registers a specific IInboxStorage instance for the Inbox pattern.
    /// </summary>
    /// <param name="storage">The inbox storage instance to use</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a pre-configured inbox storage instance.
    ///
    /// Example:
    /// <code>
    /// var storage = new CustomInboxStorage(connectionString);
    /// storageBuilder.UseInboxStorage(storage);
    /// </code>
    /// </remarks>
    public IStorageBuilder UseInboxStorage(IInboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    /// <summary>
    /// Registers a custom IQueueStorage implementation for queue processing.
    /// </summary>
    /// <typeparam name="T">The custom queue storage implementation type</typeparam>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a custom queue storage implementation.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.UseQueueStorage&lt;RabbitMqQueueStorage&gt;();
    /// </code>
    /// </remarks>
    public IStorageBuilder UseQueueStorage<T>() where T : class, IQueueStorage
    {
        _services.AddSingleton<IQueueStorage, T>();
        return this;
    }

    /// <summary>
    /// Registers a specific IQueueStorage instance for queue processing.
    /// </summary>
    /// <param name="storage">The queue storage instance to use</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Use this to register a pre-configured queue storage instance.
    ///
    /// Example:
    /// <code>
    /// var storage = new CustomQueueStorage(connectionString);
    /// storageBuilder.UseQueueStorage(storage);
    /// </code>
    /// </remarks>
    public IStorageBuilder UseQueueStorage(IQueueStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    /// <summary>
    /// Enables connection pooling for database storage providers.
    /// </summary>
    /// <param name="maxPoolSize">Maximum number of connections in the pool (default: 100)</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Connection pooling improves performance by reusing database connections.
    /// Configure pool size based on expected concurrent workload.
    ///
    /// Recommended settings:
    /// - Low traffic: 10-50 connections
    /// - Medium traffic: 50-100 connections
    /// - High traffic: 100-200 connections
    ///
    /// Example:
    /// <code>
    /// storageBuilder.WithConnectionPooling(maxPoolSize: 100);
    /// </code>
    /// </remarks>
    public IStorageBuilder WithConnectionPooling(int maxPoolSize = 100)
    {
        _services.Configure<StorageConnectionOptions>(options =>
        {
            options.MaxPoolSize = maxPoolSize;
            options.EnablePooling = true;
        });
        return this;
    }

    /// <summary>
    /// Enables automatic retry for transient storage failures.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="retryDelay">Delay between retries (default: 1 second)</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Retry logic helps handle transient failures such as:
    /// - Network timeouts
    /// - Database deadlocks
    /// - Temporary connection failures
    ///
    /// Uses exponential backoff: delay, delay*2, delay*4, etc.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.WithRetry(
    ///     maxRetries: 3,
    ///     retryDelay: TimeSpan.FromSeconds(2)
    /// );
    /// </code>
    /// </remarks>
    public IStorageBuilder WithRetry(int maxRetries = 3, TimeSpan? retryDelay = null)
    {
        _services.Configure<StorageRetryOptions>(options =>
        {
            options.MaxRetries = maxRetries;
            options.RetryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
            options.EnableRetry = true;
        });
        return this;
    }

    /// <summary>
    /// Enables circuit breaker pattern to prevent cascading failures.
    /// </summary>
    /// <param name="failureThreshold">Number of failures before opening circuit (default: 5)</param>
    /// <param name="breakDuration">How long to keep circuit open (default: 1 minute)</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Circuit breaker pattern protects the system when storage is degraded:
    /// - Closed: Normal operation
    /// - Open: Fails fast after threshold reached
    /// - Half-Open: Tests if storage has recovered
    ///
    /// This prevents overwhelming a failing storage system and allows time to recover.
    ///
    /// Example:
    /// <code>
    /// storageBuilder.WithCircuitBreaker(
    ///     failureThreshold: 5,
    ///     breakDuration: TimeSpan.FromMinutes(1)
    /// );
    /// </code>
    /// </remarks>
    public IStorageBuilder WithCircuitBreaker(int failureThreshold = 5, TimeSpan breakDuration = default)
    {
        _services.Configure<StorageCircuitBreakerOptions>(options =>
        {
            options.FailureThreshold = failureThreshold;
            options.BreakDuration = breakDuration == default ? TimeSpan.FromMinutes(1) : breakDuration;
            options.EnableCircuitBreaker = true;
        });
        return this;
    }

    /// <summary>
    /// Sets the default command timeout for storage operations.
    /// </summary>
    /// <param name="timeout">Command timeout duration</param>
    /// <returns>The storage builder for method chaining</returns>
    /// <remarks>
    /// Command timeout controls how long to wait for storage operations to complete.
    /// Adjust based on workload and storage performance characteristics.
    ///
    /// Recommended timeouts:
    /// - Fast operations (reads): 5-10 seconds
    /// - Normal operations: 30 seconds (default)
    /// - Batch operations: 60-120 seconds
    ///
    /// Example:
    /// <code>
    /// storageBuilder.WithCommandTimeout(TimeSpan.FromSeconds(30));
    /// </code>
    /// </remarks>
    public IStorageBuilder WithCommandTimeout(TimeSpan timeout)
    {
        _services.Configure<StorageOptions>(options =>
        {
            options.CommandTimeout = timeout;
        });
        return this;
    }

    /// <summary>
    /// Completes storage configuration and returns the service collection.
    /// </summary>
    /// <returns>The configured service collection</returns>
    /// <remarks>
    /// This method finalizes storage configuration. If no storage provider was
    /// explicitly configured, it defaults to in-memory storage.
    ///
    /// Example:
    /// <code>
    /// storageBuilder
    ///     .UseInMemory()
    ///     .WithConnectionPooling()
    ///     .Build();
    /// </code>
    /// </remarks>
    public IServiceCollection Build()
    {
        // Ensure at least in-memory storage is registered if nothing else was configured
        if (!_services.Any(s => s.ServiceType == typeof(IMessageStorage)))
            UseInMemory();

        return _services;
    }
}

// Configuration option classes
public class StorageOptions
{
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class StorageConnectionOptions : StorageOptions
{
    public bool EnablePooling { get; set; }
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 0;
    public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

public class StorageRetryOptions : StorageOptions
{
    public bool EnableRetry { get; set; }
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}

public class StorageCircuitBreakerOptions : StorageOptions
{
    public bool EnableCircuitBreaker { get; set; }
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(10);
}