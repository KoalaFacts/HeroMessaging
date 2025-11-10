using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// Extension methods for PostgreSQL storage registration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Use PostgreSQL for all storage needs with default options
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSql(this IHeroMessagingBuilder builder, string connectionString)
    {
        var options = new PostgreSqlStorageOptions { ConnectionString = connectionString };
        return UsePostgreSql(builder, options);
    }

    /// <summary>
    /// Use PostgreSQL for all storage needs with custom options
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSql(this IHeroMessagingBuilder builder, PostgreSqlStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");

        services.AddSingleton(options);
        services.AddSingleton<IMessageStorage>(sp => new PostgreSqlMessageStorage(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        services.AddSingleton<IOutboxStorage>(sp => new PostgreSqlOutboxStorage(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        services.AddSingleton<IDeadLetterQueue>(sp => new PostgreSqlDeadLetterQueue(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));

        return builder;
    }

    /// <summary>
    /// Use PostgreSQL for all storage with configuration action
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSql(this IHeroMessagingBuilder builder, string connectionString, Action<PostgreSqlStorageOptions> configure)
    {
        var options = new PostgreSqlStorageOptions { ConnectionString = connectionString };
        configure(options);
        return UsePostgreSql(builder, options);
    }

    /// <summary>
    /// Use PostgreSQL for message storage only
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSqlMessageStorage(this IHeroMessagingBuilder builder, PostgreSqlStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IMessageStorage>(sp => new PostgreSqlMessageStorage(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        return builder;
    }

    /// <summary>
    /// Use PostgreSQL for outbox pattern
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSqlOutbox(this IHeroMessagingBuilder builder, PostgreSqlStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IOutboxStorage>(sp => new PostgreSqlOutboxStorage(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        return builder;
    }

    /// <summary>
    /// Use PostgreSQL for dead letter queue
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSqlDeadLetterQueue(this IHeroMessagingBuilder builder, PostgreSqlStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IDeadLetterQueue>(sp => new PostgreSqlDeadLetterQueue(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        return builder;
    }

    /// <summary>
    /// Use PostgreSQL for saga repository
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSqlSagaRepository<TSaga>(this IHeroMessagingBuilder builder, PostgreSqlStorageOptions options)
        where TSaga : class, ISaga
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<ISagaRepository<TSaga>>(sp => new PostgreSqlSagaRepository<TSaga>(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>()));
        return builder;
    }

    /// <summary>
    /// Use PostgreSQL for saga repository with default options from builder
    /// </summary>
    public static IHeroMessagingBuilder UsePostgreSqlSagaRepository<TSaga>(this IHeroMessagingBuilder builder)
        where TSaga : class, ISaga
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<ISagaRepository<TSaga>>(sp =>
        {
            var options = sp.GetRequiredService<PostgreSqlStorageOptions>();
            return new PostgreSqlSagaRepository<TSaga>(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IJsonSerializer>());
        });
        return builder;
    }
}
