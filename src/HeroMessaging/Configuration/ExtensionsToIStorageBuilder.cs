using System;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Configuration; // For StorageOptions
using HeroMessaging.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Configuration; // Matching target interface namespace

/// <summary>
/// Extension methods for IStorageBuilder to support database storage options
/// </summary>
public static class ExtensionsToIStorageBuilder
{
    /// <summary>
    /// Use SQL Server storage
    /// </summary>
    public static IStorageBuilder UseSqlServer(
        this IStorageBuilder builder, 
        string connectionString, 
        Action<StorageOptions>? configure = null)
    {
        var options = new StorageOptions();
        configure?.Invoke(options);
        
        // These would use the actual SQL Server implementations when available
        // For now, using in-memory storage as placeholders
        builder.UseMessageStorage(new InMemoryMessageStorage());
        builder.UseOutboxStorage(new InMemoryOutboxStorage());
        builder.UseInboxStorage(new InMemoryInboxStorage());
        builder.UseQueueStorage(new InMemoryQueueStorage());
        
        return builder;
    }
    
    /// <summary>
    /// Use PostgreSQL storage
    /// </summary>
    public static IStorageBuilder UsePostgreSql(
        this IStorageBuilder builder,
        string connectionString,
        Action<StorageOptions>? configure = null)
    {
        var options = new StorageOptions();
        configure?.Invoke(options);
        
        // These would use the actual PostgreSQL implementations when available
        // For now, using in-memory storage as placeholders
        builder.UseMessageStorage(new InMemoryMessageStorage());
        builder.UseOutboxStorage(new InMemoryOutboxStorage());
        builder.UseInboxStorage(new InMemoryInboxStorage());
        builder.UseQueueStorage(new InMemoryQueueStorage());
        
        return builder;
    }
}