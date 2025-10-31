using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Processing;
using HeroMessaging.Processing.Decorators;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring transaction management in HeroMessaging
/// </summary>
public static class TransactionExtensions
{
    /// <summary>
    /// Adds transaction management to all message processors
    /// Requires IUnitOfWorkFactory to be registered in the service collection
    /// </summary>
    public static IHeroMessagingBuilder WithTransactions(
        this IHeroMessagingBuilder builder,
        IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;

        // Decorate command processor with transaction support
        services.Decorate<ICommandProcessor>((inner, serviceProvider) =>
            new TransactionCommandProcessorDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionCommandProcessorDecorator>>(),
                defaultIsolationLevel));

        // Decorate query processor with transaction support (read-only transactions)
        services.Decorate<IQueryProcessor>((inner, serviceProvider) =>
            new TransactionQueryProcessorDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionQueryProcessorDecorator>>()));

        // Decorate event bus with transaction support
        services.Decorate<IEventBus>((inner, serviceProvider) =>
            new TransactionEventBusDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionEventBusDecorator>>(),
                defaultIsolationLevel));

        // Decorate outbox processor with transaction support
        services.Decorate<Processing.IOutboxProcessor>((inner, serviceProvider) =>
            new TransactionOutboxProcessorDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionOutboxProcessorDecorator>>(),
                defaultIsolationLevel));

        // Decorate inbox processor with transaction support
        services.Decorate<Processing.IInboxProcessor>((inner, serviceProvider) =>
            new TransactionInboxProcessorDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionInboxProcessorDecorator>>(),
                defaultIsolationLevel));

        return builder;
    }

    /// <summary>
    /// Adds transaction management only to command processing
    /// Useful when you want transactions only for write operations
    /// </summary>
    public static IHeroMessagingBuilder WithCommandTransactions(
        this IHeroMessagingBuilder builder,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;

        services.Decorate<ICommandProcessor>((inner, serviceProvider) =>
            new TransactionCommandProcessorDecorator(
                inner,
                serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                serviceProvider.GetRequiredService<ILogger<TransactionCommandProcessorDecorator>>(),
                isolationLevel));

        return builder;
    }

    /// <summary>
    /// Adds transaction management with custom isolation levels per processor type
    /// </summary>
    public static IHeroMessagingBuilder WithTransactions(
        this IHeroMessagingBuilder builder,
        TransactionConfiguration config)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;

        if (config.CommandIsolationLevel.HasValue)
        {
            services.Decorate<ICommandProcessor>((inner, serviceProvider) =>
                new TransactionCommandProcessorDecorator(
                    inner,
                    serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                    serviceProvider.GetRequiredService<ILogger<TransactionCommandProcessorDecorator>>(),
                    config.CommandIsolationLevel.Value));
        }

        if (config.QueryIsolationLevel.HasValue)
        {
            services.Decorate<IQueryProcessor>((inner, serviceProvider) =>
                new TransactionQueryProcessorDecorator(
                    inner,
                    serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                    serviceProvider.GetRequiredService<ILogger<TransactionQueryProcessorDecorator>>()));
        }

        if (config.EventIsolationLevel.HasValue)
        {
            services.Decorate<IEventBus>((inner, serviceProvider) =>
                new TransactionEventBusDecorator(
                    inner,
                    serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                    serviceProvider.GetRequiredService<ILogger<TransactionEventBusDecorator>>(),
                    config.EventIsolationLevel.Value));
        }

        if (config.OutboxIsolationLevel.HasValue)
        {
            services.Decorate<Processing.IOutboxProcessor>((inner, serviceProvider) =>
                new TransactionOutboxProcessorDecorator(
                    inner,
                    serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                    serviceProvider.GetRequiredService<ILogger<TransactionOutboxProcessorDecorator>>(),
                    config.OutboxIsolationLevel.Value));
        }

        if (config.InboxIsolationLevel.HasValue)
        {
            services.Decorate<Processing.IInboxProcessor>((inner, serviceProvider) =>
                new TransactionInboxProcessorDecorator(
                    inner,
                    serviceProvider.GetRequiredService<IUnitOfWorkFactory>(),
                    serviceProvider.GetRequiredService<ILogger<TransactionInboxProcessorDecorator>>(),
                    config.InboxIsolationLevel.Value));
        }

        return builder;
    }
}

/// <summary>
/// Configuration for transaction isolation levels per processor type
/// </summary>
public class TransactionConfiguration
{
    /// <summary>
    /// Gets or sets the transaction isolation level for command processing. Default is ReadCommitted.
    /// </summary>
    public IsolationLevel? CommandIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Gets or sets the transaction isolation level for query processing. Default is ReadCommitted.
    /// </summary>
    public IsolationLevel? QueryIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Gets or sets the transaction isolation level for event processing. Default is ReadCommitted.
    /// </summary>
    public IsolationLevel? EventIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Gets or sets the transaction isolation level for outbox processing. Default is ReadCommitted.
    /// </summary>
    public IsolationLevel? OutboxIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Gets or sets the transaction isolation level for inbox processing. Default is ReadCommitted.
    /// </summary>
    public IsolationLevel? InboxIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Creates a configuration for write operations only (commands, outbox, inbox)
    /// </summary>
    public static TransactionConfiguration WriteOperationsOnly()
    {
        return new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.ReadCommitted,
            QueryIsolationLevel = null, // No transactions for reads
            EventIsolationLevel = IsolationLevel.ReadCommitted,
            OutboxIsolationLevel = IsolationLevel.ReadCommitted,
            InboxIsolationLevel = IsolationLevel.ReadCommitted
        };
    }

    /// <summary>
    /// Creates a configuration with serializable isolation for critical operations
    /// </summary>
    public static TransactionConfiguration Serializable()
    {
        return new TransactionConfiguration
        {
            CommandIsolationLevel = IsolationLevel.Serializable,
            QueryIsolationLevel = IsolationLevel.ReadCommitted,
            EventIsolationLevel = IsolationLevel.Serializable,
            OutboxIsolationLevel = IsolationLevel.Serializable,
            InboxIsolationLevel = IsolationLevel.Serializable
        };
    }
}