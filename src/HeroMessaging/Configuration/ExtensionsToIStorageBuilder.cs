using HeroMessaging.Configuration; // For StorageOptions

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
        throw new NotImplementedException(
            "SQL Server storage is not yet implemented. " +
            "Please use the HeroMessaging.Storage.SqlServer package and call UseSqlServer on IHeroMessagingBuilder instead.");
    }

    /// <summary>
    /// Use PostgreSQL storage
    /// </summary>
    public static IStorageBuilder UsePostgreSql(
        this IStorageBuilder builder,
        string connectionString,
        Action<StorageOptions>? configure = null)
    {
        throw new NotImplementedException(
            "PostgreSQL storage is not yet implemented. " +
            "Please use the HeroMessaging.Storage.PostgreSQL package and call UsePostgreSql on IHeroMessagingBuilder instead.");
    }
}