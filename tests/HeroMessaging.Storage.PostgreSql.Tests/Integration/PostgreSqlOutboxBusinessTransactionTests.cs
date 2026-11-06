using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// F06 evidence: an Outbox entry written through <see cref="PostgreSqlUnitOfWork"/> commits and rolls back
/// together with business rows written on the unit of work's own connection and transaction.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlOutboxBusinessTransactionTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task Commit_BusinessRowAndOutboxEntryBecomeVisibleTogether()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var worker = CreateWorker(connectionString);
            var message = TestMessageBuilder.CreateValidMessage("order placed");

            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await InsertOrderAsync(unitOfWork, "order-1");
                await unitOfWork.OutboxStorage.AddAsync(message, new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);

                Assert.Equal(0, await CountOrdersAsync(connectionString));
                using (var timeout = CreateLockTimeout())
                {
                    // A worker polling while the business transaction is open must neither see nor wait for the entry.
                    Assert.Empty(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), timeout.Token));
                }

                await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
            }

            Assert.Equal(1, await CountOrdersAsync(connectionString));
            var claim = Assert.Single(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
            Assert.Equal(message.MessageId, claim.Entry.Message.MessageId);
            Assert.Equal("orders", claim.Entry.Options.Destination);
        });
    }

    [Fact]
    public async Task Rollback_DiscardsBusinessRowAndOutboxEntry()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var worker = CreateWorker(connectionString);

            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await InsertOrderAsync(unitOfWork, "order-2");
                await unitOfWork.OutboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage(),
                    new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);
                await unitOfWork.RollbackAsync(TestContext.Current.CancellationToken);
            }

            Assert.Equal(0, await CountOrdersAsync(connectionString));
            Assert.Empty(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task DisposeWithoutCommit_DiscardsBusinessRowAndOutboxEntry()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var worker = CreateWorker(connectionString);

            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await InsertOrderAsync(unitOfWork, "order-3");
                await unitOfWork.OutboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage(),
                    new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);
            }

            Assert.Equal(0, await CountOrdersAsync(connectionString));
            Assert.Empty(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task FailedBusinessStatement_RollsBackOutboxEntryWrittenEarlier()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var worker = CreateWorker(connectionString);

            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await unitOfWork.OutboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage(),
                    new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);
                await InsertOrderAsync(unitOfWork, "order-4");

                await Assert.ThrowsAsync<PostgresException>(() => InsertOrderAsync(unitOfWork, "order-4"));
                await unitOfWork.RollbackAsync(TestContext.Current.CancellationToken);
            }

            Assert.Equal(0, await CountOrdersAsync(connectionString));
            Assert.Empty(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Transaction_IsNullOutsideATransactionAndFollowsEachNewTransaction()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var unitOfWork = new PostgreSqlUnitOfWork(connectionString);
            Assert.Null(unitOfWork.Transaction);

            await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            var first = unitOfWork.Transaction;
            Assert.NotNull(first);
            Assert.Same(unitOfWork.Connection, first.Connection);
            await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Null(unitOfWork.Transaction);

            await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(unitOfWork.Transaction);
            Assert.Same(unitOfWork.Connection, unitOfWork.Transaction.Connection);
            await InsertOrderAsync(unitOfWork, "order-5");
            await unitOfWork.RollbackAsync(TestContext.Current.CancellationToken);
            Assert.Null(unitOfWork.Transaction);
            Assert.Equal(0, await CountOrdersAsync(connectionString));
        });
    }

    [Fact]
    public async Task ConcurrentBusinessTransactions_EachAddOutboxEntriesWithoutWaitingForTheOther()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var worker = CreateWorker(connectionString);
            await using var first = new PostgreSqlUnitOfWork(connectionString);
            await using var second = new PostgreSqlUnitOfWork(connectionString);
            await first.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            await second.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);

            await first.OutboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage("first"),
                new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);
            using (var timeout = CreateLockTimeout())
            {
                await second.OutboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage("second"),
                    new OutboxOptions { Destination = "orders" }, timeout.Token);
            }

            await second.CommitAsync(TestContext.Current.CancellationToken);
            using (var timeout = CreateLockTimeout())
            {
                Assert.Single(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), timeout.Token));
            }

            await first.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Single(await worker.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Initialization_TableMissingLeaseColumns_StillAddsThem()
    {
        var database = $"outbox_old_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {database}");
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database, Pooling = false }.ConnectionString;

        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = new NpgsqlCommand("""
                    CREATE TABLE outbox_messages (
                        id VARCHAR(100) PRIMARY KEY, message_type VARCHAR(500) NOT NULL, payload JSONB NOT NULL,
                        destination VARCHAR(200), status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                        retry_count INTEGER NOT NULL DEFAULT 0, max_retries INTEGER NOT NULL DEFAULT 3,
                        created_at TIMESTAMPTZ NOT NULL, processed_at TIMESTAMPTZ, next_retry_at TIMESTAMPTZ, last_error TEXT)
                    """, connection);
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var worker = CreateWorker(connectionString);
            await worker.AddAsync(TestMessageBuilder.CreateValidMessage(), new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);

            Assert.Single(await worker.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        }
        finally
        {
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    [Theory]
    [InlineData("Biz", "My_Outbox")]
    [InlineData("public", "outbox_messages_with_a_table_name_long_enough_to_truncate")]
    public async Task CompleteTableWithCustomName_SecondWriterDoesNotWaitForAnOpenTransaction(string schema, string table)
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var options = new PostgreSqlStorageOptions { ConnectionString = connectionString, Schema = schema, OutboxTableName = table };
            var serializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
            await new PostgreSqlOutboxStorage(options, TimeProvider.System, serializer).GetPendingAsync(1, TestContext.Current.CancellationToken);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var holder = new PostgreSqlOutboxStorage(options, TimeProvider.System, serializer, new PostgreSqlConnectionProvider(connection, transaction));
            await holder.AddAsync(TestMessageBuilder.CreateValidMessage("held"), new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);

            using var timeout = CreateLockTimeout();
            var other = new PostgreSqlOutboxStorage(options, TimeProvider.System, serializer);
            await other.AddAsync(TestMessageBuilder.CreateValidMessage("other"), new OutboxOptions { Destination = "orders" }, timeout.Token);
            Assert.Single(await other.ClaimExternalAsync(10, TimeSpan.FromMinutes(1), timeout.Token));

            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        });
    }

    private static CancellationTokenSource CreateLockTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private static PostgreSqlOutboxStorage CreateWorker(string connectionString)
        => new(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));

    private static async Task InsertOrderAsync(PostgreSqlUnitOfWork unitOfWork, string orderId)
    {
        await using var command = new NpgsqlCommand("INSERT INTO orders (id) VALUES (@id)", unitOfWork.Connection, unitOfWork.Transaction);
        command.Parameters.AddWithValue("id", orderId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountOrdersAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM orders", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// Runs the test against a fresh database so the unit of work's default Outbox table is isolated
    /// from other tests sharing the server.
    /// </summary>
    private async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var database = $"outbox_uow_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {database}");
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database, Pooling = false }.ConnectionString;

        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = new NpgsqlCommand("CREATE TABLE orders (id TEXT PRIMARY KEY)", connection);
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            // The worker creates the Outbox table outside any business transaction, as a host would at start-up.
            await CreateWorker(connectionString).GetPendingAsync(1, TestContext.Current.CancellationToken);
            await test(connectionString);
        }
        finally
        {
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
