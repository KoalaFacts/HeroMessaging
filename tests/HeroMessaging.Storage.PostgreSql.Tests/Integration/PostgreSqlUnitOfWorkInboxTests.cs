using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// Inbox entries admitted inside a <see cref="PostgreSqlUnitOfWork"/>, so the entry commits or rolls back with
/// the business state (persist before ACK).
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlUnitOfWorkInboxTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task ConcurrentUnitsOfWork_AdmitToCompleteInboxTableWithoutWaitingForEachOther()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var standalone = new PostgreSqlInboxStorage(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System,
                new DefaultJsonSerializer(new DefaultBufferPoolManager()));
            await standalone.GetPendingAsync(new InboxQuery { Limit = 1 }, TestContext.Current.CancellationToken);

            await using var first = new PostgreSqlUnitOfWork(connectionString);
            await using var second = new PostgreSqlUnitOfWork(connectionString);
            await first.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
            await second.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(await first.InboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage("first"), new InboxOptions(), TestContext.Current.CancellationToken));
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                Assert.NotNull(await second.InboxStorage.AddAsync(TestMessageBuilder.CreateValidMessage("second"), new InboxOptions(), timeout.Token));
            }

            await second.CommitAsync(TestContext.Current.CancellationToken);
            await first.CommitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, (await standalone.GetPendingAsync(new InboxQuery { Limit = 10 }, TestContext.Current.CancellationToken)).Count());
        });
    }

    [Fact]
    public async Task UnitOfWork_RolledBack_LeavesNoInboxEntry()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var message = TestMessageBuilder.CreateValidMessage("rolled back");
            await using (var unitOfWork = new PostgreSqlUnitOfWork(connectionString))
            {
                await unitOfWork.BeginTransactionAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.NotNull(await unitOfWork.InboxStorage.AddAsync(message, new InboxOptions(), TestContext.Current.CancellationToken));
                await unitOfWork.RollbackAsync(TestContext.Current.CancellationToken);
            }

            var standalone = new PostgreSqlInboxStorage(new PostgreSqlStorageOptions { ConnectionString = connectionString }, TimeProvider.System,
                new DefaultJsonSerializer(new DefaultBufferPoolManager()));
            Assert.Null(await standalone.GetAsync(message.MessageId.ToString(), TestContext.Current.CancellationToken));
            Assert.NotNull(await standalone.AddAsync(message, new InboxOptions(), TestContext.Current.CancellationToken));
        });
    }

    [Theory]
    [InlineData("Biz", "My_Inbox")]
    [InlineData("public", "inbox_messages_with_a_table_name_long_enough_to_truncate")]
    public async Task CompleteTableWithCustomName_SecondAdmissionDoesNotWaitForAnOpenTransaction(string schema, string table)
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var options = new PostgreSqlStorageOptions { ConnectionString = connectionString, Schema = schema, InboxTableName = table };
            var serializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
            await new PostgreSqlInboxStorage(options, TimeProvider.System, serializer).GetPendingAsync(new InboxQuery { Limit = 1 }, TestContext.Current.CancellationToken);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var holder = new PostgreSqlInboxStorage(options, TimeProvider.System, serializer, new PostgreSqlConnectionProvider(connection, transaction));
            Assert.NotNull(await holder.AddAsync(TestMessageBuilder.CreateValidMessage("held"), new InboxOptions(), TestContext.Current.CancellationToken));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var other = new PostgreSqlInboxStorage(options, TimeProvider.System, serializer);
            Assert.NotNull(await other.AddAsync(TestMessageBuilder.CreateValidMessage("other"), new InboxOptions(), timeout.Token));

            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        });
    }

    private async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var database = $"uow_inbox_{Guid.NewGuid():N}";
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
