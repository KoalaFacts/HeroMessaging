using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// Queue and message storages created by <see cref="PostgreSqlUnitOfWork"/>. They used to create their table
/// on a new connection built from the open connection's ConnectionString, which Npgsql strips of its password,
/// so every call failed against a password-authenticated server. Schema work now runs on the shared
/// connection, and is skipped when the table is complete: a no-op <c>CREATE INDEX IF NOT EXISTS</c> still takes
/// a <c>ShareLock</c> held until commit, which would block other transactions' inserts.
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlUnitOfWorkSchemaLockTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task ConcurrentUnitsOfWork_EnqueueToCompleteQueueTableWithoutWaitingForEachOther()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var serializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
            var standalone = new PostgreSqlQueueStorage(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System, serializer);
            await standalone.GetQueuesAsync(TestContext.Current.CancellationToken);

            await using var first = new PostgreSqlUnitOfWork(connectionString);
            await using var second = new PostgreSqlUnitOfWork(connectionString);
            await first.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            await second.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);

            await first.QueueStorage.EnqueueAsync("orders", TestMessageBuilder.CreateValidMessage("first"), cancellationToken: TestContext.Current.CancellationToken);
            using (var timeout = CreateLockTimeout())
            {
                await second.QueueStorage.EnqueueAsync("orders", TestMessageBuilder.CreateValidMessage("second"), cancellationToken: timeout.Token);
            }

            await second.CommitAsync(TestContext.Current.CancellationToken);
            await first.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, await standalone.GetQueueDepthAsync("orders", TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task ConcurrentUnitsOfWork_StoreToCompleteMessageTableWithoutWaitingForEachOther()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var serializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
            var standalone = new PostgreSqlMessageStorage(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System, serializer);
            await standalone.CountAsync(cancellationToken: TestContext.Current.CancellationToken);

            await using var first = new PostgreSqlUnitOfWork(connectionString);
            await using var second = new PostgreSqlUnitOfWork(connectionString);
            await first.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            await second.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);

            await first.MessageStorage.StoreAsync(TestMessageBuilder.CreateValidMessage("first"), options: null, cancellationToken: TestContext.Current.CancellationToken);
            using (var timeout = CreateLockTimeout())
            {
                await second.MessageStorage.StoreAsync(TestMessageBuilder.CreateValidMessage("second"), options: null, cancellationToken: timeout.Token);
            }

            await second.CommitAsync(TestContext.Current.CancellationToken);
            await first.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, await standalone.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task UnitOfWork_QueueTableMissing_CreatesItInTheTransaction()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await unitOfWork.QueueStorage.EnqueueAsync("orders", TestMessageBuilder.CreateValidMessage(), cancellationToken: TestContext.Current.CancellationToken);
                await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
            }

            var standalone = new PostgreSqlQueueStorage(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System,
                new DefaultJsonSerializer(new DefaultBufferPoolManager()));
            Assert.Equal(1, await standalone.GetQueueDepthAsync("orders", TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task UnitOfWork_MessageTableMissingAndRolledBack_LeavesNothingBehind()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                await unitOfWork.MessageStorage.StoreAsync(TestMessageBuilder.CreateValidMessage(), options: null, cancellationToken: TestContext.Current.CancellationToken);
                await unitOfWork.RollbackAsync(TestContext.Current.CancellationToken);
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("SELECT to_regclass('public.messages') IS NULL", connection);
            Assert.True((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        });
    }

    private static CancellationTokenSource CreateLockTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var database = $"uow_locks_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {database}");
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database, Pooling = false }.ConnectionString;

        try
        {
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
