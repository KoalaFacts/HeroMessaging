using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Integration;

/// <summary>
/// Integration tests for PostgreSQL storage implementation
/// Tests store/retrieve, connection resilience, transactions, concurrency, and queries
/// </summary>
[Trait("Category", "Integration")]
public class PostgreSqlStorageIntegrationTests : PostgreSqlIntegrationTestBase
{
    [Fact]
    public async Task PostgreSqlStorage_StoreAndRetrieveMessage_WorksCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var message = TestMessageBuilder.CreateValidMessage("PostgreSQL test message");

        // Act
        await storage.StoreAsync(message, (IStorageTransaction?)null);
        var retrievedMessage = await storage.RetrieveAsync(message.MessageId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(message.MessageId, retrievedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(message, retrievedMessage);
        Assert.Equal(message.Timestamp, retrievedMessage.Timestamp);
    }

    [Fact]
    public async Task PostgreSqlStorage_WithTransactionCommit_CommitsCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Commit test 1"),
            TestMessageBuilder.CreateValidMessage("Commit test 2")
        };

        // Act
        using var transaction = await storage.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await storage.StoreAsync(messages[0], transaction, TestContext.Current.CancellationToken);
        await storage.StoreAsync(messages[1], transaction, TestContext.Current.CancellationToken);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert - Messages should be visible after commit
        var retrievedMessage1 = await storage.RetrieveAsync(messages[0].MessageId, TestContext.Current.CancellationToken);
        var retrievedMessage2 = await storage.RetrieveAsync(messages[1].MessageId, TestContext.Current.CancellationToken);

        Assert.NotNull(retrievedMessage1);
        Assert.NotNull(retrievedMessage2);
        Assert.Equal(messages[0].MessageId, retrievedMessage1.MessageId);
        Assert.Equal(messages[1].MessageId, retrievedMessage2.MessageId);
    }

    [Fact]
    public async Task PostgreSqlStorage_WithHighConcurrency_HandlesCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        const int concurrentOperations = 50;
        var messages = new List<IMessage>();

        for (int i = 0; i < concurrentOperations; i++)
        {
            messages.Add(TestMessageBuilder.CreateValidMessage($"Concurrent message {i}"));
        }

        // Act
        var storeTasks = messages.Select(msg => storage.StoreAsync(msg, (IStorageTransaction?)null)).ToArray();
        await Task.WhenAll(storeTasks);

        var retrieveTasks = messages.Select(msg => storage.RetrieveAsync(msg.MessageId, null)).ToArray();
        var retrievedMessages = await Task.WhenAll(retrieveTasks);

        // Assert
        Assert.Equal(concurrentOperations, retrievedMessages.Length);
        Assert.All(retrievedMessages, Assert.NotNull);

        // Verify all messages were stored and retrieved correctly
        for (int i = 0; i < concurrentOperations; i++)
        {
            var original = messages[i];
            var retrieved = retrievedMessages.First(m => m?.MessageId == original.MessageId);
            Assert.NotNull(retrieved);
            TestMessageExtensions.AssertSameContent(original, retrieved);
        }
    }

    [Fact]
    public async Task PostgreSqlStorage_QueryMessages_WithFiltering_ReturnsCorrectResults()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var baseTime = DateTimeOffset.UtcNow;
        var messages = new[]
        {
            CreateMessageWithTimestamp("Query test 1", baseTime),
            CreateMessageWithTimestamp("Query test 2", baseTime.AddMinutes(1)),
            CreateMessageWithTimestamp("Different content", baseTime.AddMinutes(2)),
            CreateMessageWithTimestamp("Query test 3", baseTime.AddMinutes(3))
        };

        foreach (var message in messages)
        {
            await storage.StoreAsync(message, (IStorageTransaction?)null);
        }

        // Act
        var queryResult = await storage.QueryAsync(new MessageQuery
        {
            ContentContains = "Query test",
            FromTimestamp = baseTime,
            ToTimestamp = baseTime.AddMinutes(5),
            MaxResults = 10
        });

        // Assert
        Assert.Equal(3, queryResult.Count); // Should find 3 messages with "Query test"
        Assert.All(queryResult, msg =>
        {
            Assert.Contains("Query test", msg.GetTestContent() ?? "");
        });
    }

    [Fact]
    public async Task PostgreSqlStorage_DeleteMessage_RemovesFromStorage()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var message = TestMessageBuilder.CreateValidMessage("Delete test message");

        // Act
        await storage.StoreAsync(message, (IStorageTransaction?)null);

        // Verify it exists
        var retrievedBeforeDelete = await storage.RetrieveAsync(message.MessageId, null, TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedBeforeDelete);

        // Delete it
        await storage.DeleteAsync(message.MessageId, TestContext.Current.CancellationToken);

        // Verify it's gone
        var retrievedAfterDelete = await storage.RetrieveAsync(message.MessageId, TestContext.Current.CancellationToken);
        Assert.Null(retrievedAfterDelete);
    }

    private IMessage CreateMessageWithTimestamp(string content, DateTimeOffset timestamp)
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: timestamp,
            correlationId: null,
            causationId: null,
            content: content,
            metadata: new Dictionary<string, object>
            {
                ["TestType"] = "QueryTest"
            }
        );
    }
}
