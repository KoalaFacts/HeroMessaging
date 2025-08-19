using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Core.Processing;
using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.HealthChecks;

public static class ServiceCollectionExtensions
{
    public static IHealthChecksBuilder AddHeroMessagingHealthChecks(
        this IHealthChecksBuilder builder,
        Action<HeroMessagingHealthCheckOptions>? configure = null)
    {
        var options = new HeroMessagingHealthCheckOptions();
        configure?.Invoke(options);

        if (options.CheckMessageStorage)
        {
            builder.Services.AddTransient<MessageStorageHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_message_storage",
                sp => sp.GetRequiredService<MessageStorageHealthCheck>(),
                options.MessageStorageFailureStatus,
                options.Tags));
        }

        if (options.CheckOutboxStorage)
        {
            builder.Services.AddTransient<OutboxStorageHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_outbox_storage",
                sp => sp.GetRequiredService<OutboxStorageHealthCheck>(),
                options.OutboxStorageFailureStatus,
                options.Tags));
        }

        if (options.CheckInboxStorage)
        {
            builder.Services.AddTransient<InboxStorageHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_inbox_storage",
                sp => sp.GetRequiredService<InboxStorageHealthCheck>(),
                options.InboxStorageFailureStatus,
                options.Tags));
        }

        if (options.CheckQueueStorage)
        {
            builder.Services.AddTransient<QueueStorageHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_queue_storage",
                sp => sp.GetRequiredService<QueueStorageHealthCheck>(),
                options.QueueStorageFailureStatus,
                options.Tags));
        }

        if (options.CheckCommandProcessor)
        {
            builder.Services.AddTransient<CommandProcessorHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_command_processor",
                sp => sp.GetRequiredService<CommandProcessorHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.CheckEventBus)
        {
            builder.Services.AddTransient<EventBusHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_event_bus",
                sp => sp.GetRequiredService<EventBusHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.CheckQueryProcessor)
        {
            builder.Services.AddTransient<QueryProcessorHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_query_processor",
                sp => sp.GetRequiredService<QueryProcessorHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.CheckQueueProcessor)
        {
            builder.Services.AddTransient<QueueProcessorHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_queue_processor",
                sp => sp.GetRequiredService<QueueProcessorHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.CheckOutboxProcessor)
        {
            builder.Services.AddTransient<OutboxProcessorHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_outbox_processor",
                sp => sp.GetRequiredService<OutboxProcessorHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.CheckInboxProcessor)
        {
            builder.Services.AddTransient<InboxProcessorHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging_inbox_processor",
                sp => sp.GetRequiredService<InboxProcessorHealthCheck>(),
                options.ProcessorFailureStatus,
                options.Tags));
        }

        if (options.AddCompositeCheck)
        {
            builder.Services.AddTransient<HeroMessagingHealthCheck>();
            builder.Add(new HealthCheckRegistration(
                "hero_messaging",
                sp => sp.GetRequiredService<HeroMessagingHealthCheck>(),
                HealthStatus.Unhealthy,
                options.Tags));
        }

        if (options.AddReadinessCheck)
        {
            builder.Services.AddTransient<ReadinessCheck>();
            builder.Add(new HealthCheckRegistration(
                "ready",
                sp => sp.GetRequiredService<ReadinessCheck>(),
                HealthStatus.Unhealthy,
                new[] { "ready" }));
        }

        if (options.AddLivenessCheck)
        {
            builder.Services.AddTransient<LivenessCheck>();
            builder.Add(new HealthCheckRegistration(
                "live",
                sp => sp.GetRequiredService<LivenessCheck>(),
                HealthStatus.Unhealthy,
                new[] { "live" }));
        }

        return builder;
    }

    public static IHealthChecksBuilder AddMessageStorageHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "message_storage",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<MessageStorageHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new MessageStorageHealthCheck(
                sp.GetRequiredService<IMessageStorage>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddOutboxStorageHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "outbox_storage",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<OutboxStorageHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new OutboxStorageHealthCheck(
                sp.GetRequiredService<IOutboxStorage>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddInboxStorageHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "inbox_storage",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<InboxStorageHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new InboxStorageHealthCheck(
                sp.GetRequiredService<IInboxStorage>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddQueueStorageHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "queue_storage",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<QueueStorageHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new QueueStorageHealthCheck(
                sp.GetRequiredService<IQueueStorage>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddCommandProcessorHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "command_processor",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<CommandProcessorHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new CommandProcessorHealthCheck(
                sp.GetRequiredService<CommandProcessor>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddEventBusHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "event_bus",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<EventBusHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new EventBusHealthCheck(
                sp.GetRequiredService<EventBus>(),
                name),
            failureStatus,
            tags));
    }

    public static IHealthChecksBuilder AddQueryProcessorHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "query_processor",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddTransient<QueryProcessorHealthCheck>();
        
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new QueryProcessorHealthCheck(
                sp.GetRequiredService<QueryProcessor>(),
                name),
            failureStatus,
            tags));
    }
}

public class HeroMessagingHealthCheckOptions
{
    public bool CheckMessageStorage { get; set; } = true;
    public bool CheckOutboxStorage { get; set; } = true;
    public bool CheckInboxStorage { get; set; } = true;
    public bool CheckQueueStorage { get; set; } = true;
    public bool CheckCommandProcessor { get; set; } = true;
    public bool CheckEventBus { get; set; } = true;
    public bool CheckQueryProcessor { get; set; } = true;
    public bool CheckQueueProcessor { get; set; } = true;
    public bool CheckOutboxProcessor { get; set; } = true;
    public bool CheckInboxProcessor { get; set; } = true;
    public bool AddCompositeCheck { get; set; } = true;
    public bool AddReadinessCheck { get; set; } = true;
    public bool AddLivenessCheck { get; set; } = true;
    
    public HealthStatus? MessageStorageFailureStatus { get; set; } = HealthStatus.Unhealthy;
    public HealthStatus? OutboxStorageFailureStatus { get; set; } = HealthStatus.Unhealthy;
    public HealthStatus? InboxStorageFailureStatus { get; set; } = HealthStatus.Unhealthy;
    public HealthStatus? QueueStorageFailureStatus { get; set; } = HealthStatus.Unhealthy;
    public HealthStatus? ProcessorFailureStatus { get; set; } = HealthStatus.Degraded;
    
    public IEnumerable<string> Tags { get; set; } = new[] { "messaging", "hero" };
}