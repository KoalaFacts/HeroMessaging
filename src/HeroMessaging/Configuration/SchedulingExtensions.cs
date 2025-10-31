using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Configuration;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Extension methods for configuring message scheduling.
/// </summary>
public static class SchedulingExtensions
{
    /// <summary>
    /// Adds message scheduling capabilities to HeroMessaging.
    /// </summary>
    /// <param name="builder">The messaging builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// By default, this configures an in-memory scheduler suitable for development and testing.
    /// For production use, call UseStorageBackedScheduler() after this method.
    /// </remarks>
    public static IHeroMessagingBuilder WithScheduling(
        this IHeroMessagingBuilder builder,
        Action<ISchedulingBuilder>? configure = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        var services = builder.Build();
        var schedulingBuilder = new SchedulingBuilder(services);

        // Register default components
        services.TryAddSingleton<IMessageDeliveryHandler, DefaultMessageDeliveryHandler>();
        services.TryAddSingleton<IScheduledMessageStorage, InMemoryScheduledMessageStorage>();

        // Default to in-memory scheduler
        services.TryAddSingleton<IMessageScheduler>(sp =>
        {
            var deliveryHandler = sp.GetRequiredService<IMessageDeliveryHandler>();
            return new InMemoryScheduler(deliveryHandler);
        });

        // Allow custom configuration
        configure?.Invoke(schedulingBuilder);

        return builder;
    }

    /// <summary>
    /// Configures the in-memory scheduler explicitly.
    /// </summary>
    /// <param name="builder">The scheduling builder</param>
    /// <returns>The builder for method chaining</returns>
    public static ISchedulingBuilder UseInMemoryScheduler(this ISchedulingBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Services.AddSingleton<IMessageScheduler>(sp =>
        {
            var deliveryHandler = sp.GetRequiredService<IMessageDeliveryHandler>();
            return new InMemoryScheduler(deliveryHandler);
        });

        return builder;
    }

    /// <summary>
    /// Configures a storage-backed scheduler for production use.
    /// </summary>
    /// <param name="builder">The scheduling builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for method chaining</returns>
    public static ISchedulingBuilder UseStorageBackedScheduler(
        this ISchedulingBuilder builder,
        Action<StorageBackedSchedulerOptions>? configure = null)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        var options = new StorageBackedSchedulerOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMessageScheduler, StorageBackedScheduler>();

        // Ensure storage is registered
        builder.Services.TryAddSingleton<IScheduledMessageStorage, InMemoryScheduledMessageStorage>();

        return builder;
    }
}

/// <summary>
/// Builder interface for configuring message scheduling capabilities in HeroMessaging.
/// </summary>
/// <remarks>
/// This builder provides access to the service collection for configuring:
/// - Message scheduler implementations (in-memory or storage-backed)
/// - Scheduled message storage providers
/// - Message delivery handlers
/// - Polling intervals and batch processing options
///
/// The builder is obtained through the WithScheduling() extension method on IHeroMessagingBuilder.
/// Use the extension methods (UseInMemoryScheduler, UseStorageBackedScheduler) to configure
/// the scheduling implementation.
///
/// Example usage:
/// <code>
/// builder.WithScheduling(scheduling =>
/// {
///     scheduling.UseStorageBackedScheduler(options =>
///     {
///         options.PollingInterval = TimeSpan.FromSeconds(5);
///         options.BatchSize = 50;
///     });
/// });
/// </code>
/// </remarks>
public interface ISchedulingBuilder
{
    /// <summary>
    /// Gets the service collection for registering scheduling-related services.
    /// </summary>
    /// <remarks>
    /// Use this property for advanced scenarios where you need to register custom
    /// implementations of IMessageScheduler, IScheduledMessageStorage, or
    /// IMessageDeliveryHandler directly.
    ///
    /// Example:
    /// <code>
    /// schedulingBuilder.Services.AddSingleton&lt;IScheduledMessageStorage, MyCustomStorage&gt;();
    /// </code>
    /// </remarks>
    IServiceCollection Services { get; }
}

/// <summary>
/// Internal implementation of scheduling builder.
/// </summary>
internal sealed class SchedulingBuilder : ISchedulingBuilder
{
    public SchedulingBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}

/// <summary>
/// Configuration options for storage-backed scheduler.
/// </summary>
public class StorageBackedSchedulerOptions
{
    /// <summary>
    /// Gets or sets the interval for polling the storage for due messages.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of messages to retrieve in each polling batch.
    /// Default is 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of concurrent message deliveries.
    /// Default is 10.
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to automatically clean up delivered/cancelled messages.
    /// Default is true.
    /// </summary>
    public bool AutoCleanup { get; set; } = true;

    /// <summary>
    /// Gets or sets the age threshold for cleaning up old messages.
    /// Default is 24 hours.
    /// </summary>
    public TimeSpan CleanupAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the interval for running cleanup.
    /// Default is 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
