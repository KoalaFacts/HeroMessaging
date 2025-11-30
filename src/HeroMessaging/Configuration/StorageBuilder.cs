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

    public StorageBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IStorageBuilder UseInMemory()
    {
        _services.AddSingleton<IMessageStorage, InMemoryMessageStorage>();
        _services.AddSingleton<IOutboxStorage, InMemoryOutboxStorage>();
        _services.AddSingleton<IInboxStorage, InMemoryInboxStorage>();
        _services.AddSingleton<IQueueStorage, InMemoryQueueStorage>();


        return this;
    }

    public IStorageBuilder UseInMemory(Action<InMemoryStorageOptions> configure)
    {
        var options = new InMemoryStorageOptions();
        configure(options);

        _services.AddSingleton(options);
        return UseInMemory();
    }

    public IStorageBuilder UseMessageStorage<T>() where T : class, IMessageStorage
    {
        _services.AddSingleton<IMessageStorage, T>();
        return this;
    }

    public IStorageBuilder UseMessageStorage(IMessageStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    public IStorageBuilder UseOutboxStorage<T>() where T : class, IOutboxStorage
    {
        _services.AddSingleton<IOutboxStorage, T>();
        return this;
    }

    public IStorageBuilder UseOutboxStorage(IOutboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    public IStorageBuilder UseInboxStorage<T>() where T : class, IInboxStorage
    {
        _services.AddSingleton<IInboxStorage, T>();
        return this;
    }

    public IStorageBuilder UseInboxStorage(IInboxStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    public IStorageBuilder UseQueueStorage<T>() where T : class, IQueueStorage
    {
        _services.AddSingleton<IQueueStorage, T>();
        return this;
    }

    public IStorageBuilder UseQueueStorage(IQueueStorage storage)
    {
        _services.AddSingleton(storage);
        return this;
    }

    public IStorageBuilder WithConnectionPooling(int maxPoolSize = 100)
    {
        _services.Configure<StorageConnectionOptions>(options =>
        {
            options.MaxPoolSize = maxPoolSize;
            options.EnablePooling = true;
        });
        return this;
    }

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

    public IStorageBuilder WithCommandTimeout(TimeSpan timeout)
    {
        _services.Configure<StorageOptions>(options =>
        {
            options.CommandTimeout = timeout;
        });
        return this;
    }

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
