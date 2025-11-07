using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests;

/// <summary>
/// Shared fixture for SQL Server idempotency store integration tests.
/// Creates one SQL Server container that is shared across all tests in the collection.
/// </summary>
public sealed class SqlServerIdempotencyStoreFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // Create and start SQL Server container once for all tests
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong@Passw0rd")
            .Build();

        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Initialize database schema
        await InitializeDatabaseSchemaAsync(ConnectionString);
    }

    private static async Task InitializeDatabaseSchemaAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string createTableSql = """
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[IdempotencyResponses]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [dbo].[IdempotencyResponses] (
                    [IdempotencyKey] NVARCHAR(450) NOT NULL,
                    [Status] TINYINT NOT NULL,
                    [SuccessResult] NVARCHAR(MAX) NULL,
                    [FailureType] NVARCHAR(500) NULL,
                    [FailureMessage] NVARCHAR(MAX) NULL,
                    [FailureStackTrace] NVARCHAR(MAX) NULL,
                    [StoredAt] DATETIME2(7) NOT NULL,
                    [ExpiresAt] DATETIME2(7) NOT NULL,
                    CONSTRAINT [PK_IdempotencyResponses] PRIMARY KEY CLUSTERED ([IdempotencyKey] ASC)
                );

                CREATE NONCLUSTERED INDEX [IX_IdempotencyResponses_ExpiresAt]
                    ON [dbo].[IdempotencyResponses] ([ExpiresAt] ASC);

                CREATE NONCLUSTERED INDEX [IX_IdempotencyResponses_Status_StoredAt]
                    ON [dbo].[IdempotencyResponses] ([Status] ASC, [StoredAt] DESC)
                    INCLUDE ([ExpiresAt]);
            END
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Stop and dispose container when all tests are done
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// Collection definition for SQL Server idempotency store tests.
/// All tests in this collection will share the same SQL Server container.
/// </summary>
[CollectionDefinition(nameof(SqlServerIdempotencyStoreCollection))]
public class SqlServerIdempotencyStoreCollection : ICollectionFixture<SqlServerIdempotencyStoreFixture>
{
    // This class is never instantiated. It exists only to define the collection and fixture association.
}
