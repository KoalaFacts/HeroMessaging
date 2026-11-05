using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("SQL Server storage integration")]
public sealed class SqlServerInboxDeduplicationWindowTests : SqlServerIntegrationTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddAsync_ExpiredWindowWithRetainedId_PreservesOriginalWork(bool processed)
    {
        var token = TestContext.Current.CancellationToken;
        var options = new SqlServerStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = "dbo",
            InboxTableName = $"InboxWindow_{Guid.NewGuid():N}"
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero));
        var storage = new SqlServerInboxStorage(options, clock,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        var intakeOptions = new InboxOptions { DeduplicationWindow = TimeSpan.FromMinutes(1) };
        var original = TestMessageBuilder.CreateValidMessage("original durable work");
        var collision = new TestMessage(
            messageId: original.MessageId,
            timestamp: original.Timestamp,
            correlationId: null,
            causationId: null,
            content: "duplicate must not overwrite",
            metadata: []);

        try
        {
            Assert.NotNull(await storage.AddAsync(original, intakeOptions, token));
            if (processed)
                Assert.True(await storage.MarkProcessedAsync(original.MessageId.ToString(), token));

            clock.Advance(TimeSpan.FromMinutes(2));

            // A retained primary-key row remains authoritative after the
            // configured window expires; never overwrite unfinished work.
            Assert.Null(await storage.AddAsync(collision, intakeOptions, token));

            var retained = Assert.IsType<InboxEntry>(
                await storage.GetAsync(original.MessageId.ToString(), token));
            Assert.Equal(processed ? InboxStatus.Processed : InboxStatus.Pending, retained.Status);
            Assert.Equal("original durable work", retained.Message.GetTestContent());
            var pending = await storage.GetUnprocessedAsync(100, token);
            if (processed)
                Assert.DoesNotContain(pending, entry => entry.Id == original.MessageId.ToString());
            else
                Assert.Contains(pending, entry => entry.Id == original.MessageId.ToString());
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new SqlCommand(
                $"DROP TABLE IF EXISTS [dbo].[{options.InboxTableName}]", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AddAsync_ConcurrentSameId_AdmitsExactlyOneMessage()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new SqlServerStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = "dbo",
            InboxTableName = $"InboxConcurrent_{Guid.NewGuid():N}"
        };
        var storage = new SqlServerInboxStorage(options, TimeProvider.System,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        var message = TestMessageBuilder.CreateValidMessage("single durable work");
        var intakeOptions = new InboxOptions { DeduplicationWindow = TimeSpan.FromMinutes(1) };

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 12)
                .Select(_ => storage.AddAsync(message, intakeOptions, token)));
            Assert.Single(results, entry => entry is not null);
            var retained = Assert.IsType<InboxEntry>(
                await storage.GetAsync(message.MessageId.ToString(), token));
            Assert.Equal(InboxStatus.Pending, retained.Status);
            Assert.Equal("single durable work", retained.Message.GetTestContent());
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new SqlCommand(
                $"DROP TABLE IF EXISTS [dbo].[{options.InboxTableName}]", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AddAsync_SharedConnectionWithNoCountOn_DoesNotAdmitRetainedDuplicate()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new SqlServerStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = "dbo",
            InboxTableName = $"InboxNoCount_{Guid.NewGuid():N}"
        };
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using (var noCount = new SqlCommand("SET NOCOUNT ON", connection))
            await noCount.ExecuteNonQueryAsync(token);

        var storage = new SqlServerInboxStorage(options, TimeProvider.System,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()),
            new SqlServerConnectionProvider(connection));
        var original = TestMessageBuilder.CreateValidMessage("original shared-connection work");
        var duplicate = new TestMessage(
            messageId: original.MessageId,
            timestamp: original.Timestamp,
            correlationId: null,
            causationId: null,
            content: "must not be admitted",
            metadata: []);
        var intakeOptions = new InboxOptions { RequireIdempotency = true };

        try
        {
            Assert.NotNull(await storage.AddAsync(original, intakeOptions, token));
            Assert.Null(await storage.AddAsync(duplicate, intakeOptions, token));

            var retained = Assert.IsType<InboxEntry>(
                await storage.GetAsync(original.MessageId.ToString(), token));
            Assert.Equal(InboxStatus.Pending, retained.Status);
            Assert.Equal("original shared-connection work", retained.Message.GetTestContent());
        }
        finally
        {
            await using var dropTable = new SqlCommand(
                $"DROP TABLE IF EXISTS [dbo].[{options.InboxTableName}]", connection);
            await dropTable.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AddAsync_ExistingTableWithInsertAuditTrigger_AdmitsOnceAndPreservesPendingWork()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var inboxTable = $"InboxTriggered_{suffix}";
        var auditTable = $"InboxAudit_{suffix}";
        var triggerName = $"InboxAuditTrigger_{suffix}";
        var options = new SqlServerStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = "dbo",
            InboxTableName = inboxTable
        };
        var serializer = new DefaultJsonSerializer(new DefaultBufferPoolManager());
        var setupStorage = new SqlServerInboxStorage(options, TimeProvider.System, serializer);

        try
        {
            // Initialize a real table before installing a trigger, like an
            // externally managed table where AutoCreateTables is disabled.
            var seed = TestMessageBuilder.CreateValidMessage("initialize existing table");
            Assert.NotNull(await setupStorage.AddAsync(seed, new InboxOptions(), token));

            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync(token);
                await using (var createAudit = new SqlCommand(
                    $"CREATE TABLE [dbo].[{auditTable}] (MessageId NVARCHAR(100) NOT NULL)", connection))
                    await createAudit.ExecuteNonQueryAsync(token);

                // SQL Server requires CREATE TRIGGER to be the first statement
                // in its batch. The trigger performs actual audit persistence.
                await using var createTrigger = new SqlCommand($"""
                    CREATE TRIGGER [dbo].[{triggerName}]
                    ON [dbo].[{inboxTable}]
                    AFTER INSERT
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        INSERT INTO [dbo].[{auditTable}] (MessageId)
                        SELECT Id FROM inserted;
                    END
                    """, connection);
                await createTrigger.ExecuteNonQueryAsync(token);
            }

            var externalOptions = new SqlServerStorageOptions
            {
                ConnectionString = ConnectionString,
                Schema = "dbo",
                InboxTableName = inboxTable,
                AutoCreateTables = false
            };
            var storage = new SqlServerInboxStorage(externalOptions, TimeProvider.System, serializer);
            var intakeOptions = new InboxOptions { RequireIdempotency = true };
            var original = TestMessageBuilder.CreateValidMessage("audited durable work");
            var duplicate = new TestMessage(
                messageId: original.MessageId,
                timestamp: original.Timestamp,
                correlationId: null,
                causationId: null,
                content: "duplicate must not be stored or audited",
                metadata: []);

            Assert.NotNull(await storage.AddAsync(original, intakeOptions, token));
            Assert.Null(await storage.AddAsync(duplicate, intakeOptions, token));

            var retained = Assert.IsType<InboxEntry>(
                await storage.GetAsync(original.MessageId.ToString(), token));
            Assert.Equal(InboxStatus.Pending, retained.Status);
            Assert.Equal("audited durable work", retained.Message.GetTestContent());

            await using var auditConnection = new SqlConnection(ConnectionString);
            await auditConnection.OpenAsync(token);
            await using var auditCount = new SqlCommand(
                $"SELECT COUNT(*) FROM [dbo].[{auditTable}] WHERE MessageId = @MessageId", auditConnection);
            auditCount.Parameters.Add("@MessageId", System.Data.SqlDbType.NVarChar, 100)
                .Value = original.MessageId.ToString();
            Assert.Equal(1, Assert.IsType<int>(await auditCount.ExecuteScalarAsync(token)));
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var cleanup = new SqlCommand($"""
                DROP TABLE IF EXISTS [dbo].[{inboxTable}];
                DROP TABLE IF EXISTS [dbo].[{auditTable}];
                """, connection);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

}
