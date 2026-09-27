using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

[Trait("Category", "Integration")]
public class PostgreSqlOutboxLeaseTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task ClaimExternalAsync_OnlyOneWorkerGetsMessageAndRestoresConcreteType()
    {
        await WithStorageAsync(async (storage, options, time) =>
        {
            var message = TestMessageBuilder.CreateValidMessage("leased message");
            await storage.AddAsync(message, new OutboxOptions
            {
                Destination = "orders",
                RetryDelay = TimeSpan.FromSeconds(7)
            }, TestContext.Current.CancellationToken);

            var otherWorker = CreateStorage(options, time);
            var claims = await Task.WhenAll(
                storage.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken),
                otherWorker.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

            var claim = Assert.Single(claims.SelectMany(static result => result));
            var restored = Assert.IsType<TestMessage>(claim.Entry.Message);
            Assert.Equal(message.MessageId, restored.MessageId);
            Assert.Equal(message.Content, restored.Content);
            Assert.Equal(TimeSpan.FromSeconds(7), claim.Entry.Options.RetryDelay);
            Assert.True(await storage.CompleteExternalAsync(claim.Entry.Id, claim.Token, TestContext.Current.CancellationToken));
            Assert.Empty(await otherWorker.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task ClaimExternalAsync_ExpiredLeaseCanBeRecoveredButOldOwnerCannotComplete()
    {
        await WithStorageAsync(async (storage, _, time) =>
        {
            await storage.AddAsync(TestMessageBuilder.CreateValidMessage(),
                new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);

            var first = Assert.Single(await storage.ClaimExternalAsync(1, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            time.Advance(TimeSpan.FromSeconds(6));
            var second = Assert.Single(await storage.ClaimExternalAsync(1, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            Assert.Equal(first.Entry.Id, second.Entry.Id);
            Assert.NotEqual(first.Token, second.Token);
            Assert.False(await storage.CompleteExternalAsync(first.Entry.Id, first.Token, TestContext.Current.CancellationToken));
            Assert.True(await storage.CompleteExternalAsync(second.Entry.Id, second.Token, TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task RetryExternalAsync_WaitsUntilRetryIsDue()
    {
        await WithStorageAsync(async (storage, _, time) =>
        {
            await storage.AddAsync(TestMessageBuilder.CreateValidMessage(),
                new OutboxOptions { Destination = "orders" }, TestContext.Current.CancellationToken);
            var first = Assert.Single(await storage.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
            var retryAt = time.GetUtcNow().AddMinutes(2);

            Assert.True(await storage.RetryExternalAsync(first.Entry.Id, first.Token, 1, retryAt, "temporary", TestContext.Current.CancellationToken));
            Assert.Empty(await storage.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

            time.Advance(TimeSpan.FromMinutes(2));
            var second = Assert.Single(await storage.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
            Assert.Equal(1, second.Entry.RetryCount);
            Assert.True(await storage.FailExternalAsync(second.Entry.Id, second.Token, 2, "exhausted", TestContext.Current.CancellationToken));
            Assert.Empty(await storage.ClaimExternalAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        });
    }

    private async Task WithStorageAsync(Func<PostgreSqlOutboxStorage, PostgreSqlStorageOptions, FakeTimeProvider, Task> test)
    {
        var source = Options ?? throw new InvalidOperationException("Test not initialized.");
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = source.ConnectionString,
            Schema = source.Schema,
            OutboxTableName = $"outbox_{Guid.NewGuid():N}"
        };
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var storage = CreateStorage(options, time);

        try
        {
            await test(storage, options, time);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS {options.GetFullTableName(options.OutboxTableName)}", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static PostgreSqlOutboxStorage CreateStorage(PostgreSqlStorageOptions options, TimeProvider time)
        => new(options, time, new DefaultJsonSerializer(new DefaultBufferPoolManager()));
}
