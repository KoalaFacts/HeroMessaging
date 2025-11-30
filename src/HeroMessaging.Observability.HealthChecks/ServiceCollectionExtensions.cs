using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Observability.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Diagnostics.HealthChecks;

// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIHealthChecksBuilderForHeroMessaging
{
    public static IHealthChecksBuilder AddHeroMessagingHealthChecks(
        this IHealthChecksBuilder builder,
        Action<HeroMessaging.Observability.HealthChecks.HeroMessagingHealthCheckOptions>? configure = null)
    {
        var options = new HeroMessaging.Observability.HealthChecks.HeroMessagingHealthCheckOptions();
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
        HeroMessaging.Observability.HealthChecks.HeroMessagingHealthCheckOptions options)
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
        HeroMessaging.Observability.HealthChecks.HeroMessagingHealthCheckOptions options)
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

    public static IHealthChecksBuilder AddCompositeHealthCheck(
        this IHealthChecksBuilder builder,
        string name,
        params IEnumerable<string> checkNames)
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
