using HeroMessaging.Configuration; // For StorageOptions
using HeroMessaging.Storage;

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
        // For now, using in-memory storage as placeholders with System TimeProvider
        builder.UseMessageStorage(new InMemoryMessageStorage(TimeProvider.System));
        builder.UseOutboxStorage(new InMemoryOutboxStorage(TimeProvider.System));
        builder.UseInboxStorage(new InMemoryInboxStorage(TimeProvider.System));
        builder.UseQueueStorage(new InMemoryQueueStorage(TimeProvider.System));

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
        // For now, using in-memory storage as placeholders with System TimeProvider
        builder.UseMessageStorage(new InMemoryMessageStorage(TimeProvider.System));
        builder.UseOutboxStorage(new InMemoryOutboxStorage(TimeProvider.System));
        builder.UseInboxStorage(new InMemoryInboxStorage(TimeProvider.System));
        builder.UseQueueStorage(new InMemoryQueueStorage(TimeProvider.System));

        return builder;
    }
}