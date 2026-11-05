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
public sealed class SqlServerInboxFairnessTests : SqlServerIntegrationTestBase
{
    [Fact]
    public async Task GetUnprocessedAsync_LimitedBacklog_SelectsOldestPendingMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new SqlServerStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = "dbo",
            InboxTableName = $"InboxFairness_{Guid.NewGuid():N}"
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero));
        var storage = new SqlServerInboxStorage(options, clock,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        var oldest = TestMessageBuilder.CreateValidMessage("oldest pending");
        var newest = TestMessageBuilder.CreateValidMessage("newest pending");

        try
        {
            Assert.NotNull(await storage.AddAsync(oldest, new InboxOptions(), cancellationToken));
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.NotNull(await storage.AddAsync(newest, new InboxOptions(), cancellationToken));

            // Limit must not prefer recent arrivals over unfinished older work.
            var first = Assert.Single(await storage.GetUnprocessedAsync(1, cancellationToken));
            Assert.Equal(oldest.MessageId.ToString(), first.Id);
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
}
