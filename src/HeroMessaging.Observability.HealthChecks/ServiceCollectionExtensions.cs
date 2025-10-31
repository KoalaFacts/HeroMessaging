using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Extension methods for registering HeroMessaging health checks with the application's health check system.
/// </summary>
/// <remarks>
/// This class provides fluent API methods to register health checks for HeroMessaging components including:
/// - Storage implementations (Message, Outbox, Inbox, Queue)
/// - Message transports
/// - Composite health checks
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddHeroMessagingHealthChecks(options =>
///     {
///         options.CheckStorage = true;
///         options.CheckTransport = true;
///         options.FailureStatus = HealthStatus.Degraded;
///         options.Tags = new[] { "hero-messaging", "ready" };
///     });
/// </code>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers health checks for HeroMessaging components based on the specified configuration options.
    /// </summary>
    /// <param name="builder">The health checks builder to add checks to</param>
    /// <param name="configure">Optional configuration action to customize which health checks are registered</param>
    /// <returns>The health checks builder for method chaining</returns>
    /// <remarks>
    /// This method automatically registers health checks for all enabled HeroMessaging components.
    /// Health checks are registered conditionally based on whether the corresponding services are registered in DI.
    /// If a component is not registered, the health check will report as healthy with a descriptive message.
    ///
    /// By default:
    /// - All storage health checks are enabled (CheckStorage = true)
    /// - Transport health checks are disabled (CheckTransport = false)
    /// - Failure status is set to Unhealthy
    ///
    /// Example:
    /// <code>
    /// services.AddHealthChecks()
    ///     .AddHeroMessagingHealthChecks(options =>
    ///     {
    ///         options.CheckMessageStorage = true;
    ///         options.CheckOutboxStorage = true;
    ///         options.CheckTransport = false;
    ///     });
    /// </code>
    /// </remarks>
    public static IHealthChecksBuilder AddHeroMessagingHealthChecks(
        this IHealthChecksBuilder builder,
        Action<HeroMessagingHealthCheckOptions>? configure = null)
    {
        var options = new HeroMessagingHealthCheckOptions();
        configure?.Invoke(options);

        if (options.CheckStorage)
        {
            builder.AddStorageHealthChecks(options);
        }

        if (options.CheckTransport)
        {
            builder.AddTransportHealthChecks(options);
        }

        return builder;
    }

    private static IHealthChecksBuilder AddStorageHealthChecks(
        this IHealthChecksBuilder builder,
        HeroMessagingHealthCheckOptions options)
    {
        if (options.CheckMessageStorage)
        {
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_message_storage",
                sp =>
                {
                    var storage = sp.GetService<IMessageStorage>();
                    return storage != null
                        ? new MessageStorageHealthCheck(storage, sp.GetRequiredService<TimeProvider>())
                        : new AlwaysHealthyCheck("Message storage not registered");
                },
                options.FailureStatus,
                options.Tags));
        }

        if (options.CheckOutboxStorage)
        {
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_outbox_storage",
                sp =>
                {
                    var storage = sp.GetService<IOutboxStorage>();
                    return storage != null
                        ? new OutboxStorageHealthCheck(storage)
                        : new AlwaysHealthyCheck("Outbox storage not registered");
                },
                options.FailureStatus,
                options.Tags));
        }

        if (options.CheckInboxStorage)
        {
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_inbox_storage",
                sp =>
                {
                    var storage = sp.GetService<IInboxStorage>();
                    return storage != null
                        ? new InboxStorageHealthCheck(storage)
                        : new AlwaysHealthyCheck("Inbox storage not registered");
                },
                options.FailureStatus,
                options.Tags));
        }

        if (options.CheckQueueStorage)
        {
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_queue_storage",
                sp =>
                {
                    var storage = sp.GetService<IQueueStorage>();
                    return storage != null
                        ? new QueueStorageHealthCheck(storage)
                        : new AlwaysHealthyCheck("Queue storage not registered");
                },
                options.FailureStatus,
                options.Tags));
        }

        return builder;
    }

    private static IHealthChecksBuilder AddTransportHealthChecks(
        this IHealthChecksBuilder builder,
        HeroMessagingHealthCheckOptions options)
    {
        // Register a health check that will enumerate all transports at runtime
        builder.Add(new HealthCheckRegistration(
            "hero_messaging_transport",
            sp =>
            {
                var transports = sp.GetServices<IMessageTransport>().ToList();

                if (transports.Count == 0)
                {
                    return new AlwaysHealthyCheck("Transport not registered");
                }

                if (transports.Count == 1)
                {
                    // Single transport - use simple health check
                    return new TransportHealthCheck(transports[0]);
                }

                // Multiple transports - use composite health check
                return new MultipleTransportHealthCheck(transports);
            },
            options.FailureStatus,
            options.Tags));

        return builder;
    }

    /// <summary>
    /// Registers a composite health check that aggregates multiple named health checks into a single check.
    /// </summary>
    /// <param name="builder">The health checks builder to add the composite check to</param>
    /// <param name="name">The name to register this composite check under</param>
    /// <param name="checkNames">The names of the individual health checks to aggregate</param>
    /// <returns>The health checks builder for method chaining</returns>
    /// <remarks>
    /// A composite health check allows you to group multiple health checks under a single name.
    /// This is useful for creating logical groupings like "database", "external-services", etc.
    ///
    /// Example:
    /// <code>
    /// services.AddHealthChecks()
    ///     .AddHeroMessagingHealthChecks()
    ///     .AddCompositeHealthCheck(
    ///         "hero-messaging-all",
    ///         "hero_messaging_message_storage",
    ///         "hero_messaging_outbox_storage",
    ///         "hero_messaging_transport");
    /// </code>
    /// </remarks>
    public static IHealthChecksBuilder AddCompositeHealthCheck(
        this IHealthChecksBuilder builder,
        string name,
        params string[] checkNames)
    {
        builder.Add(new HealthCheckRegistration(
            name,
            sp => new CompositeHealthCheck(checkNames),
            null,
            null));

        return builder;
    }

    private class AlwaysHealthyCheck(string description) : IHealthCheck
    {
        private readonly string _description = description;

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Healthy(_description));
        }
    }
}

/// <summary>
/// Configuration options for HeroMessaging health checks.
/// </summary>
/// <remarks>
/// Use these options to control which health checks are registered and how they behave.
/// All storage checks are enabled by default, while transport checks are disabled by default.
///
/// Example:
/// <code>
/// var options = new HeroMessagingHealthCheckOptions
/// {
///     CheckStorage = true,
///     CheckMessageStorage = true,
///     CheckOutboxStorage = false,  // Skip outbox checks
///     CheckTransport = true,
///     FailureStatus = HealthStatus.Degraded,
///     Tags = new[] { "hero-messaging", "ready" }
/// };
/// </code>
/// </remarks>
public class HeroMessagingHealthCheckOptions
{
    /// <summary>
    /// Gets or sets whether to check any storage components. When false, all individual storage checks are skipped.
    /// Default is true.
    /// </summary>
    public bool CheckStorage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check message storage (IMessageStorage) health.
    /// Only applies if CheckStorage is true. Default is true.
    /// </summary>
    public bool CheckMessageStorage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check outbox storage (IOutboxStorage) health.
    /// Only applies if CheckStorage is true. Default is true.
    /// </summary>
    public bool CheckOutboxStorage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check inbox storage (IInboxStorage) health.
    /// Only applies if CheckStorage is true. Default is true.
    /// </summary>
    public bool CheckInboxStorage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check queue storage (IQueueStorage) health.
    /// Only applies if CheckStorage is true. Default is true.
    /// </summary>
    public bool CheckQueueStorage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check message transport (IMessageTransport) health.
    /// Default is false to avoid potential network calls during health checks.
    /// </summary>
    public bool CheckTransport { get; set; } = false;

    /// <summary>
    /// Gets or sets the health status to report when a health check fails.
    /// Default is Unhealthy. Set to Degraded if you want failures to be less severe.
    /// </summary>
    public Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? FailureStatus { get; set; } = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy;

    /// <summary>
    /// Gets or sets optional tags to apply to all registered health checks.
    /// Tags can be used to filter health checks (e.g., "ready", "live", "startup").
    /// Default is null (no tags).
    /// </summary>
    public IReadOnlyCollection<string>? Tags { get; set; }
}