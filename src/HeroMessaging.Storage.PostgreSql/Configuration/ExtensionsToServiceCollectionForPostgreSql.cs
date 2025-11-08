using HeroMessaging.Abstractions.Idempotency;
using HeroMessaging.Storage.PostgreSql;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring PostgreSQL idempotency storage.
/// </summary>
public static class ExtensionsToServiceCollectionForPostgreSql
{
    /// <summary>
    /// Registers PostgreSQL as the idempotency store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="connectionString"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="connectionString"/> is empty or whitespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method registers the PostgreSQL idempotency store as a singleton service.
    /// The store provides persistent, distributed storage for idempotency responses using PostgreSQL with JSONB support.
    /// </para>
    /// <para>
    /// <strong>Prerequisites</strong>:
    /// </para>
    /// <list type="number">
    /// <item><description>PostgreSQL database must be accessible via the connection string</description></item>
    /// <item><description>The idempotency_responses table must exist (run migration script)</description></item>
    /// <item><description>Application must have read/write permissions on the table</description></item>
    /// </list>
    /// <para>
    /// <strong>Migration Script Location</strong>:
    /// </para>
    /// <code>
    /// src/HeroMessaging/Idempotency/Storage/Sql/PostgreSQL/001_create_idempotency_table.sql
    /// </code>
    /// <para>
    /// <strong>Example Usage</strong>:
    /// </para>
    /// <code>
    /// services.AddPostgreSqlIdempotencyStore("Host=localhost;Database=heromessaging;Username=postgres;Password=password");
    ///
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.WithIdempotency(idempotency =>
    ///     {
    ///         idempotency
    ///             .WithSuccessTtl(TimeSpan.FromDays(7))
    ///             .WithFailureTtl(TimeSpan.FromHours(1));
    ///     });
    /// });
    /// </code>
    /// </remarks>
    public static IServiceCollection AddPostgreSqlIdempotencyStore(
        this IServiceCollection services,
        string connectionString)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        if (connectionString == null)
            throw new ArgumentNullException(nameof(connectionString));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty or whitespace.", nameof(connectionString));

        // Register the PostgreSQL store with the connection string
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var jsonSerializer = sp.GetRequiredService<IJsonSerializer>();
            return new PostgreSqlIdempotencyStore(connectionString, timeProvider, jsonSerializer);
        });

        return services;
    }
}
