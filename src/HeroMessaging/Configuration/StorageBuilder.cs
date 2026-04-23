using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Implementation of storage builder for configuring storage plugins
/// </summary>
public class StorageBuilder : IStorageBuilder
{
    private readonly IServiceCollection _services;
    /// <summary>
    /// Initializes a new instance of the <see cref="StorageBuilder"/> class.
    /// </summary>

    public StorageBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }
    /// <summary>
    /// Executes use in memory.
    /// </summary>

    public IStorageBuilder UseInMemory()
    {
        _services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        _services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        _services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        _services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();


        return this;
    }
    /// <summary>
    /// Executes use in memory.
    /// </summary>

    public IStorageBuilder UseInMemory(Action<InMemoryStorageOptions> configure)
    {
        var options = new InMemoryStorageOptions();
        configure(options);

        _services.AddSingleton(options);
        return UseInMemory();
    }
    /// <summary>
    /// Executes use message storage.
    /// </summary>

    public IStorageBuilder UseMessageStorage<T>() where T : class, IMessageStorage
    {
        _services.AddSingleton<IMessageStorage, T>();
        return this;
    }
    /// <summary>
    /// Executes use message storage.
    /// </summary>

    public IStorageBuilder UseMessageStorage(IMessageStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }
    /// <summary>
    /// Executes use outbox storage.
    /// </summary>

    public IStorageBuilder UseOutboxStorage<T>() where T : class, IOutboxStorage
    {
        _services.AddSingleton<IOutboxStorage, T>();
        return this;
    }
    /// <summary>
    /// Executes use outbox storage.
    /// </summary>

    public IStorageBuilder UseOutboxStorage(IOutboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }
    /// <summary>
    /// Executes use inbox storage.
    /// </summary>

    public IStorageBuilder UseInboxStorage<T>() where T : class, IInboxStorage
    {
        _services.AddSingleton<IInboxStorage, T>();
        return this;
    }
    /// <summary>
    /// Executes use inbox storage.
    /// </summary>

    public IStorageBuilder UseInboxStorage(IInboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }
    /// <summary>
    /// Executes use queue storage.
    /// </summary>

    public IStorageBuilder UseQueueStorage<T>() where T : class, IQueueStorage
    {
        _services.AddSingleton<IQueueStorage, T>();
        return this;
    }
    /// <summary>
    /// Executes use queue storage.
    /// </summary>

    public IStorageBuilder UseQueueStorage(IQueueStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }
    /// <summary>
    /// Executes with connection pooling.
    /// </summary>

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
    /// Executes with retry.
    /// </summary>

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
    /// Executes with circuit breaker.
    /// </summary>

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
    /// Executes with command timeout.
    /// </summary>

    public IStorageBuilder WithCommandTimeout(TimeSpan timeout)
    {
        _services.Configure<StorageOptions>(options =>
        {
            options.CommandTimeout = timeout;
        });
        return this;
    }
    /// <summary>
    /// Executes build.
    /// </summary>

    public IServiceCollection Build()
    {
        // Ensure at least in-memory storage is registered if nothing else was configured
        if (!_services.Any(s => s.ServiceType == typeof(IMessageStorage)))
            UseInMemory();

        return _services;
    }
}
/// <summary>
/// Represents the storage options type.
/// </summary>

// Configuration option classes
public class StorageOptions
{
    /// <summary>
    /// Gets or sets command timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
/// <summary>
/// Represents the storage connection options type.
/// </summary>

public class StorageConnectionOptions : StorageOptions
{
    /// <summary>
    /// Gets or sets enable pooling.
    /// </summary>
    public bool EnablePooling { get; set; }
    /// <summary>
    /// Gets or sets max pool size.
    /// </summary>
    public int MaxPoolSize { get; set; } = 100;
    /// <summary>
    /// Gets or sets min pool size.
    /// </summary>
    public int MinPoolSize { get; set; } = 0;
    /// <summary>
    /// Gets or sets connection lifetime.
    /// </summary>
    public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
/// <summary>
/// Represents the storage retry options type.
/// </summary>

public class StorageRetryOptions : StorageOptions
{
    /// <summary>
    /// Gets or sets enable retry.
    /// </summary>
    public bool EnableRetry { get; set; }
    /// <summary>
    /// Gets or sets max retries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>
    /// Gets or sets retry delay.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>
    /// Gets or sets max retry delay.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}
/// <summary>
/// Represents the storage circuit breaker options type.
/// </summary>

public class StorageCircuitBreakerOptions : StorageOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether circuit breaking is enabled.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; }

    /// <summary>
    /// Gets or sets the number of failures required to open the circuit.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the length of time that the circuit remains open.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the time window used to sample failures for the circuit.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(10);
}
