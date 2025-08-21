using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Builder for configuring storage plugins
/// </summary>
public interface IStorageBuilder
{
    /// <summary>
    /// Use in-memory storage (default)
    /// </summary>
    IStorageBuilder UseInMemory();
    
    /// <summary>
    /// Use in-memory storage with specific options
    /// </summary>
    IStorageBuilder UseInMemory(Action<InMemoryStorageOptions> configure);
    
    /// <summary>
    /// Use custom message storage implementation
    /// </summary>
    IStorageBuilder UseMessageStorage<T>() where T : class, IMessageStorage;
    
    /// <summary>
    /// Use custom message storage instance
    /// </summary>
    IStorageBuilder UseMessageStorage(IMessageStorage storage);
    
    /// <summary>
    /// Use custom outbox storage implementation
    /// </summary>
    IStorageBuilder UseOutboxStorage<T>() where T : class, IOutboxStorage;
    
    /// <summary>
    /// Use custom outbox storage instance
    /// </summary>
    IStorageBuilder UseOutboxStorage(IOutboxStorage storage);
    
    /// <summary>
    /// Use custom inbox storage implementation
    /// </summary>
    IStorageBuilder UseInboxStorage<T>() where T : class, IInboxStorage;
    
    /// <summary>
    /// Use custom inbox storage instance
    /// </summary>
    IStorageBuilder UseInboxStorage(IInboxStorage storage);
    
    /// <summary>
    /// Use custom queue storage implementation
    /// </summary>
    IStorageBuilder UseQueueStorage<T>() where T : class, IQueueStorage;
    
    /// <summary>
    /// Use custom queue storage instance
    /// </summary>
    IStorageBuilder UseQueueStorage(IQueueStorage storage);
    
    /// <summary>
    /// Enable connection pooling
    /// </summary>
    IStorageBuilder WithConnectionPooling(int maxPoolSize = 100);
    
    /// <summary>
    /// Enable automatic retry on transient failures
    /// </summary>
    IStorageBuilder WithRetry(int maxRetries = 3, TimeSpan? retryDelay = null);
    
    /// <summary>
    /// Enable circuit breaker for storage operations
    /// </summary>
    IStorageBuilder WithCircuitBreaker(int failureThreshold = 5, TimeSpan breakDuration = default);
    
    /// <summary>
    /// Set command timeout for storage operations
    /// </summary>
    IStorageBuilder WithCommandTimeout(TimeSpan timeout);
    
    /// <summary>
    /// Build and return the service collection
    /// </summary>
    IServiceCollection Build();
}

/// <summary>
/// Options for in-memory storage
/// </summary>
public class InMemoryStorageOptions
{
    public int MaxMessages { get; set; } = 10000;
    public TimeSpan MessageRetention { get; set; } = TimeSpan.FromHours(24);
    public bool EnableMetrics { get; set; } = false;
    public bool EnableCaching { get; set; } = true;
}