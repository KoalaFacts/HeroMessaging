using Testcontainers.PostgreSql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests;

/// <summary>
/// Shared fixture for PostgreSQL idempotency store integration tests.
/// Creates one PostgreSQL container that is shared across all tests in the collection.
/// </summary>
public sealed class PostgreSqlIdempotencyStoreFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // Create and start PostgreSQL container once for all tests
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Initialize database schema
        await InitializeDatabaseSchemaAsync(ConnectionString);
    }

    private static async Task InitializeDatabaseSchemaAsync(string connectionString)
    {
        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string createTableSql = """
            CREATE TABLE IF NOT EXISTS idempotency_responses (
                idempotency_key VARCHAR(450) NOT NULL,
                status SMALLINT NOT NULL,
                success_result JSONB NULL,
                failure_type VARCHAR(500) NULL,
                failure_message TEXT NULL,
                failure_stack_trace TEXT NULL,
                stored_at TIMESTAMP NOT NULL,
                expires_at TIMESTAMP NOT NULL,
                CONSTRAINT pk_idempotency_responses PRIMARY KEY (idempotency_key)
            );

            CREATE INDEX IF NOT EXISTS idx_idempotency_responses_expires_at
                ON idempotency_responses(expires_at);

            CREATE INDEX IF NOT EXISTS idx_idempotency_responses_status_stored_at
                ON idempotency_responses(status, stored_at DESC)
                INCLUDE (expires_at);
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
/// Collection definition for PostgreSQL idempotency store tests.
/// All tests in this collection will share the same PostgreSQL container.
/// </summary>
[CollectionDefinition(nameof(PostgreSqlIdempotencyStoreCollection))]
public class PostgreSqlIdempotencyStoreCollection : ICollectionFixture<PostgreSqlIdempotencyStoreFixture>
{
    // This class is never instantiated. It exists only to define the collection and fixture association.
}
