using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

public static class ServiceCollectionExtensions
{
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
                    return new AlwaysHealthyCheck("No transports registered");
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

public class HeroMessagingHealthCheckOptions
{
    public bool CheckStorage { get; set; } = true;
    public bool CheckMessageStorage { get; set; } = true;
    public bool CheckOutboxStorage { get; set; } = true;
    public bool CheckInboxStorage { get; set; } = true;
    public bool CheckQueueStorage { get; set; } = true;
    public bool CheckTransport { get; set; } = true;
    public HealthStatus? FailureStatus { get; set; } = HealthStatus.Unhealthy;
    public IReadOnlyCollection<string>? Tags { get; set; }
}