using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlInboxDeduplicationWindowTests : PostgreSqlIntegrationTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddAsync_ExpiredWindowWithRetainedId_PreservesOriginalWork(bool processed)
    {
        var token = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_window_{Guid.NewGuid():N}"
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero));
        var storage = new PostgreSqlInboxStorage(options, clock,
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

            // The deduplication window has elapsed but the primary-key record
            // is still retained. A redelivery must never raise a SQL constraint
            // error, overwrite its payload, or hide unfinished business work.
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AddAsync_ConcurrentSameId_AdmitsExactlyOneMessage()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_concurrent_{Guid.NewGuid():N}"
        };
        var storage = new PostgreSqlInboxStorage(options, TimeProvider.System,
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {options.Schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_EntryInsideItsDeduplicationWindow_IsRetainedAndStillRejectsRedelivery()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = ConnectionString,
            Schema = $"inbox_retention_{Guid.NewGuid():N}"
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));
        var storage = new PostgreSqlInboxStorage(options, clock,
            new DefaultJsonSerializer(new DefaultBufferPoolManager()));
        var longWindow = new InboxOptions { DeduplicationWindow = TimeSpan.FromDays(30) };
        var shortWindow = new InboxOptions { DeduplicationWindow = TimeSpan.FromHours(1) };
        var guarded = TestMessageBuilder.CreateValidMessage("inside its window");
        var expired = TestMessageBuilder.CreateValidMessage("past its window");
        var unbounded = TestMessageBuilder.CreateValidMessage("no window");

        try
        {
            Assert.NotNull(await storage.AddAsync(guarded, longWindow, token));
            Assert.NotNull(await storage.AddAsync(expired, shortWindow, token));
            Assert.NotNull(await storage.AddAsync(unbounded, new InboxOptions(), token));
            foreach (var message in new[] { guarded, expired, unbounded })
                Assert.True(await storage.MarkProcessedAsync(message.MessageId.ToString(), token));

            clock.Advance(TimeSpan.FromDays(8));
            await storage.CleanupOldEntriesAsync(TimeSpan.FromDays(7), token);

            // Retention never shortens an entry's own deduplication window: a
            // redelivery on day 8 of a 30-day window is still rejected.
            Assert.NotNull(await storage.GetAsync(guarded.MessageId.ToString(), token));
            Assert.Null(await storage.AddAsync(guarded, longWindow, token));
            Assert.Null(await storage.GetAsync(expired.MessageId.ToString(), token));
            Assert.Null(await storage.GetAsync(unbounded.MessageId.ToString(), token));

            clock.Advance(TimeSpan.FromDays(23));
            await storage.CleanupOldEntriesAsync(TimeSpan.FromDays(7), token);
            Assert.Null(await storage.GetAsync(guarded.MessageId.ToString(), token));
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
