using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// Extension methods for SQL Server storage registration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Use SQL Server for all storage needs with default options
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServer(this IHeroMessagingBuilder builder, string connectionString)
    {
        var options = new SqlServerStorageOptions { ConnectionString = connectionString };
        return UseSqlServer(builder, options);
    }
    
    /// <summary>
    /// Use SQL Server for all storage needs with custom options
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServer(this IHeroMessagingBuilder builder, SqlServerStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        
        services.AddSingleton(options);
        services.AddSingleton<IMessageStorage>(new SqlServerMessageStorage(options));
        services.AddSingleton<IOutboxStorage>(new SqlServerOutboxStorage(options));
        services.AddSingleton<IDeadLetterQueue>(new SqlServerDeadLetterQueue(options));
        
        return builder;
    }
    
    /// <summary>
    /// Use SQL Server for all storage with configuration action
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServer(this IHeroMessagingBuilder builder, string connectionString, Action<SqlServerStorageOptions> configure)
    {
        var options = new SqlServerStorageOptions { ConnectionString = connectionString };
        configure(options);
        return UseSqlServer(builder, options);
    }
    
    /// <summary>
    /// Use SQL Server for message storage only
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServerMessageStorage(this IHeroMessagingBuilder builder, SqlServerStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IMessageStorage>(new SqlServerMessageStorage(options));
        return builder;
    }
    
    /// <summary>
    /// Use SQL Server for outbox pattern
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServerOutbox(this IHeroMessagingBuilder builder, SqlServerStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IOutboxStorage>(new SqlServerOutboxStorage(options));
        return builder;
    }
    
    /// <summary>
    /// Use SQL Server for dead letter queue
    /// </summary>
    public static IHeroMessagingBuilder UseSqlServerDeadLetterQueue(this IHeroMessagingBuilder builder, SqlServerStorageOptions options)
    {
        var services = builder as IServiceCollection ?? throw new InvalidOperationException("Builder must implement IServiceCollection");
        services.AddSingleton<IDeadLetterQueue>(new SqlServerDeadLetterQueue(options));
        return builder;
    }
}