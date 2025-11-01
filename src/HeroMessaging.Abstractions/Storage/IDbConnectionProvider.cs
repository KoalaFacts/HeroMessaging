using System.Data.Common;

namespace HeroMessaging.Abstractions.Storage;

/// <summary>
/// Provides database connection and transaction management for storage implementations
/// </summary>
/// <typeparam name="TConnection">The database connection type</typeparam>
/// <typeparam name="TTransaction">The database transaction type</typeparam>
public interface IDbConnectionProvider<TConnection, TTransaction>
    where TConnection : DbConnection
    where TTransaction : DbTransaction
{
    /// <summary>
    /// Gets a database connection. Caller is responsible for disposing if IsSharedConnection is false.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A database connection</returns>
    Task<TConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current transaction, if any
    /// </summary>
    /// <returns>The current transaction or null</returns>
    TTransaction? GetTransaction();

    /// <summary>
    /// Indicates whether the connection is shared and should not be disposed by the caller
    /// </summary>
    bool IsSharedConnection { get; }

    /// <summary>
    /// Gets the connection string
    /// </summary>
    string ConnectionString { get; }
}
