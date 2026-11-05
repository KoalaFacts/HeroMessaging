using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlInboxFairnessTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task GetUnprocessedAsync_LimitedBacklog_SelectsOldestPendingMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_fairness_{Guid.NewGuid():N}"
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero));
        var storage = new PostgreSqlInboxStorage(options, clock,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        var oldest = TestMessageBuilder.CreateValidMessage("oldest pending");
        var newest = TestMessageBuilder.CreateValidMessage("newest pending");

        try
        {
            Assert.NotNull(await storage.AddAsync(oldest, new InboxOptions(), cancellationToken));
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.NotNull(await storage.AddAsync(newest, new InboxOptions(), cancellationToken));

            // The inbox worker polls a limited page repeatedly. Selecting newest
            // first can starve older durable business work under sustained arrivals.
            var first = Assert.Single(await storage.GetUnprocessedAsync(1, cancellationToken));
            Assert.Equal(oldest.MessageId.ToString(), first.Id);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
