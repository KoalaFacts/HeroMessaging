namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides database schema initialization and management
/// </summary>
public interface IDbSchemaInitializer
{
    /// <summary>
    /// Initializes the database schema if it doesn't exist
    /// </summary>
    /// <param name="schemaName">The schema name to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeSchemaAsync(string schemaName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a schema script (CREATE TABLE, CREATE INDEX, etc.)
    /// </summary>
    /// <param name="sql">The SQL script to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExecuteSchemaScriptAsync(string sql, CancellationToken cancellationToken = default);
}
