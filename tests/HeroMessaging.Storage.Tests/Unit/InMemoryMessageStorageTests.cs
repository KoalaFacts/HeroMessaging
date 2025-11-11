using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

/// <summary>
/// Unit tests for InMemoryMessageStorage
/// Tests in-memory storage implementation for message persistence
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryMessageStorageTests
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly InMemoryMessageStorage _storage;

    public InMemoryMessageStorageTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _storage = new InMemoryMessageStorage(_timeProvider);
    }

    #region StoreAsync Tests

    [Fact]
    public async Task StoreAsync_WithValidMessage_ReturnsMessageId()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };

        // Act
        var messageId = await _storage.StoreAsync(message);

        // Assert
        Assert.NotNull(messageId);
        Assert.NotEmpty(messageId);
    }

    [Fact]
    public async Task StoreAsync_WithOptions_StoresWithMetadata()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var options = new MessageStorageOptions
        {
            Collection = "test-collection",
            Metadata = new Dictionary<string, object> { ["key"] = "value" },
            Ttl = TimeSpan.FromHours(1)
        };

        // Act
        var messageId = await _storage.StoreAsync(message, options);

        // Assert
        Assert.NotNull(messageId);
        var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Content);
    }

    [Fact]
    public async Task StoreAsync_WithTtl_MessageExpiresAfterTtl()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Expiring" };
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(10) };

        // Act
        var messageId = await _storage.StoreAsync(message, options);

        // Assert - Message should exist before expiration
        var beforeExpiry = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.NotNull(beforeExpiry);

        // Advance time past TTL
        _timeProvider.Advance(TimeSpan.FromMinutes(11));

        // Assert - Message should be null after expiration
        var afterExpiry = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.Null(afterExpiry);
    }

    #endregion

    #region RetrieveAsync Tests

    [Fact]
    public async Task RetrieveAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _storage.RetrieveAsync<TestMessage>("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RetrieveAsync_WithValidId_ReturnsMessage()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Retrieved" };
        var messageId = await _storage.StoreAsync(message);

        // Act
        var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(message.Content, retrieved.Content);
    }

    [Fact]
    public async Task RetrieveAsync_WithWrongType_ReturnsNull()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        var messageId = await _storage.StoreAsync(message);

        // Act
        var retrieved = await _storage.RetrieveAsync<AnotherTestMessage>(messageId);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_AfterExpiration_ReturnsNull()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Expired" };
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(5) };
        var messageId = await _storage.StoreAsync(message, options);

        // Act
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId);

        // Assert
        Assert.Null(retrieved);
    }

    #endregion

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_WithEmptyQuery_ReturnsAllMessages()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Message 1" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Message 2" };
        await _storage.StoreAsync(message1);
        await _storage.StoreAsync(message2);

        var query = new MessageQuery();

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task QueryAsync_WithCollectionFilter_ReturnsOnlyMatchingCollection()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Collection A" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Collection B" };
        await _storage.StoreAsync(message1, new MessageStorageOptions { Collection = "collectionA" });
        await _storage.StoreAsync(message2, new MessageStorageOptions { Collection = "collectionB" });

        var query = new MessageQuery { Collection = "collectionA" };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Single(results);
        Assert.Equal("Collection A", results.First().Content);
    }

    [Fact]
    public async Task QueryAsync_WithTimestampRange_ReturnsMessagesInRange()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow().DateTime;
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now, Content = "Old" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(1), Content = "Recent" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(2), Content = "Future" };

        await _storage.StoreAsync(message1);
        await _storage.StoreAsync(message2);
        await _storage.StoreAsync(message3);

        var query = new MessageQuery
        {
            FromTimestamp = now.AddMinutes(30),
            ToTimestamp = now.AddHours(1).AddMinutes(30)
        };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Single(results);
        Assert.Equal("Recent", results.First().Content);
    }

    [Fact]
    public async Task QueryAsync_WithMetadataFilters_ReturnsMatchingMessages()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Tagged" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Untagged" };

        await _storage.StoreAsync(message1, new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["tag"] = "important" }
        });
        await _storage.StoreAsync(message2);

        var query = new MessageQuery
        {
            MetadataFilters = new Dictionary<string, object> { ["tag"] = "important" }
        };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Single(results);
        Assert.Equal("Tagged", results.First().Content);
    }

    [Fact]
    public async Task QueryAsync_WithOrderByTimestamp_ReturnsOrderedResults()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow().DateTime;
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(2), Content = "Latest" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now, Content = "Oldest" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(1), Content = "Middle" };

        await _storage.StoreAsync(message1);
        await _storage.StoreAsync(message2);
        await _storage.StoreAsync(message3);

        var query = new MessageQuery { OrderBy = "timestamp", Ascending = true };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        var list = results.ToList();
        Assert.Equal("Oldest", list[0].Content);
        Assert.Equal("Middle", list[1].Content);
        Assert.Equal("Latest", list[2].Content);
    }

    [Fact]
    public async Task QueryAsync_WithDescendingOrder_ReturnsReversedResults()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow().DateTime;
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now, Content = "First" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(1), Content = "Second" };

        await _storage.StoreAsync(message1);
        await _storage.StoreAsync(message2);

        var query = new MessageQuery { OrderBy = "timestamp", Ascending = false };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        var list = results.ToList();
        Assert.Equal("Second", list[0].Content);
        Assert.Equal("First", list[1].Content);
    }

    [Fact]
    public async Task QueryAsync_WithOffsetAndLimit_ReturnsPaginatedResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" };
            await _storage.StoreAsync(message);
        }

        var query = new MessageQuery { Offset = 2, Limit = 3 };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(3, results.Count());
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "To Delete" };
        var messageId = await _storage.StoreAsync(message);

        // Act
        var deleted = await _storage.DeleteAsync(messageId);

        // Assert
        Assert.True(deleted);
        var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Act
        var deleted = await _storage.DeleteAsync("non-existent-id");

        // Assert
        Assert.False(deleted);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithExistingMessage_UpdatesAndReturnsTrue()
    {
        // Arrange
        var originalMessage = new TestMessage { MessageId = Guid.NewGuid(), Content = "Original" };
        var messageId = await _storage.StoreAsync(originalMessage);

        var updatedMessage = new TestMessage { MessageId = originalMessage.MessageId, Content = "Updated" };

        // Act
        var updated = await _storage.UpdateAsync(messageId, updatedMessage);

        // Assert
        Assert.True(updated);
        var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.Content);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Update" };

        // Act
        var updated = await _storage.UpdateAsync("non-existent-id", message);

        // Assert
        Assert.False(updated);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Exists" };
        var messageId = await _storage.StoreAsync(message);

        // Act
        var exists = await _storage.ExistsAsync(messageId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentMessage_ReturnsFalse()
    {
        // Act
        var exists = await _storage.ExistsAsync("non-existent-id");

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithNoQuery_ReturnsAllCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" });
        }

        // Act
        var count = await _storage.CountAsync();

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task CountAsync_WithQuery_ReturnsFilteredCount()
    {
        // Arrange
        await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = "A" },
            new MessageStorageOptions { Collection = "collectionA" });
        await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = "A2" },
            new MessageStorageOptions { Collection = "collectionA" });
        await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = "B" },
            new MessageStorageOptions { Collection = "collectionB" });

        var query = new MessageQuery { Collection = "collectionA" };

        // Act
        var count = await _storage.CountAsync(query);

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ClearAsync_RemovesAllMessages()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" });
        }

        // Act
        await _storage.ClearAsync();

        // Assert
        var count = await _storage.CountAsync();
        Assert.Equal(0, count);
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task BeginTransactionAsync_ReturnsTransaction()
    {
        // Act
        var transaction = await _storage.BeginTransactionAsync();

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public async Task Transaction_CommitAsync_CompletesSuccessfully()
    {
        // Arrange
        var transaction = await _storage.BeginTransactionAsync();

        // Act & Assert
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Transaction_RollbackAsync_CompletesSuccessfully()
    {
        // Arrange
        var transaction = await _storage.BeginTransactionAsync();

        // Act & Assert
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Transaction_Dispose_CompletesSuccessfully()
    {
        // Arrange
        var transaction = await _storage.BeginTransactionAsync();

        // Act & Assert
        transaction.Dispose();
    }

    #endregion

    #region Explicit Interface Implementation Tests

    [Fact]
    public async Task IMessageStorage_StoreAsync_StoresMessage()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        IMessageStorage storage = _storage;

        // Act
        await storage.StoreAsync(message, (IStorageTransaction?)null);

        // Assert - Check exists via explicit interface
        var query = new MessageQuery();
        var results = await storage.QueryAsync(query);
        Assert.Contains(results, m => m.MessageId == message.MessageId);
    }

    [Fact]
    public async Task IMessageStorage_QueryAsync_ReturnsMessages()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test1" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test2" };
        await _storage.StoreAsync(message1, new MessageStorageOptions { Collection = "test" });
        await _storage.StoreAsync(message2, new MessageStorageOptions { Collection = "test" });
        IMessageStorage storage = _storage;

        var query = new MessageQuery { Collection = "test" };

        // Act
        var results = await storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task IMessageStorage_DeleteAsync_DeletesMessage()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Test" };
        await _storage.StoreAsync(message);
        IMessageStorage storage = _storage;

        // Act
        await storage.DeleteAsync(message.MessageId);

        // Assert
        var exists = await _storage.ExistsAsync(message.MessageId.ToString());
        Assert.False(exists);
    }

    #endregion

    #region Additional Coverage Tests

    [Fact]
    public async Task QueryAsync_WithUnrecognizedOrderBy_IgnoresOrderingAndReturnsAll()
    {
        // Arrange
        var now = _timeProvider.GetUtcNow().DateTime;
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(2), Content = "Latest" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now, Content = "Oldest" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Timestamp = now.AddHours(1), Content = "Middle" };

        await _storage.StoreAsync(message1);
        await _storage.StoreAsync(message2);
        await _storage.StoreAsync(message3);

        var query = new MessageQuery { OrderBy = "invalidField", Ascending = true };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert - Should return all messages unsorted when OrderBy field is invalid
        Assert.Equal(3, results.Count());
    }

    [Fact]
    public async Task QueryAsync_WithMultipleMetadataFilters_ReturnMessagesMatchingAllFilters()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "MatchBoth" };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "MatchOnly1" };
        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "MatchNeither" };

        await _storage.StoreAsync(message1, new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object>
            {
                ["tag"] = "important",
                ["priority"] = "high"
            }
        });
        await _storage.StoreAsync(message2, new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object>
            {
                ["tag"] = "important",
                ["priority"] = "low"
            }
        });
        await _storage.StoreAsync(message3, new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object>
            {
                ["tag"] = "unimportant",
                ["priority"] = "low"
            }
        });

        var query = new MessageQuery
        {
            MetadataFilters = new Dictionary<string, object>
            {
                ["tag"] = "important",
                ["priority"] = "high"
            }
        };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Single(results);
        Assert.Equal("MatchBoth", results.First().Content);
    }

    [Fact]
    public async Task QueryAsync_WithNoFilters_ReturnsAllMessages()
    {
        // Arrange - Store messages with expiration but query should return all
        var now = _timeProvider.GetUtcNow().DateTime;
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Message1", Timestamp = now };
        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Message2", Timestamp = now };

        await _storage.StoreAsync(message1, new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(5) });
        await _storage.StoreAsync(message2, new MessageStorageOptions { Ttl = TimeSpan.FromHours(1) });

        // Act
        var results = await _storage.QueryAsync<TestMessage>(new MessageQuery());

        // Assert - Both messages returned regardless of TTL (query doesn't expire)
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task RetrieveAsync_WithExpiredMessageRemovesThenReturnsNull()
    {
        // Arrange
        var message = new TestMessage { MessageId = Guid.NewGuid(), Content = "Expiring" };
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(5) };
        var messageId = await _storage.StoreAsync(message, options);

        // Verify message exists initially
        var beforeExpiry = await _storage.RetrieveAsync<TestMessage>(messageId);
        Assert.NotNull(beforeExpiry);

        // Advance time past TTL
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        // Act - First retrieval after expiration should remove and return null
        var afterExpiry = await _storage.RetrieveAsync<TestMessage>(messageId);

        // Assert - Message should be gone
        Assert.Null(afterExpiry);

        // Verify it no longer exists
        var stillExists = await _storage.ExistsAsync(messageId);
        Assert.False(stillExists);
    }

    [Fact]
    public async Task QueryAsync_WithOffsetBeyondTotalMessages_ReturnsEmpty()
    {
        // Arrange
        for (int i = 0; i < 3; i++)
        {
            await _storage.StoreAsync(new TestMessage { MessageId = Guid.NewGuid(), Content = $"Message {i}" });
        }

        var query = new MessageQuery { Offset = 10, Limit = 5 };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_OrderByStoredAtAscending_ReturnsCorrectly()
    {
        // Arrange
        var message1 = new TestMessage { MessageId = Guid.NewGuid(), Content = "First", Timestamp = DateTime.UtcNow };
        await _storage.StoreAsync(message1);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        var message2 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Second", Timestamp = DateTime.UtcNow };
        await _storage.StoreAsync(message2);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(10));

        var message3 = new TestMessage { MessageId = Guid.NewGuid(), Content = "Third", Timestamp = DateTime.UtcNow };
        await _storage.StoreAsync(message3);

        var query = new MessageQuery { OrderBy = "storedat", Ascending = true };

        // Act
        var results = await _storage.QueryAsync<TestMessage>(query);

        // Assert
        var list = results.ToList();
        Assert.Equal("First", list[0].Content);
        Assert.Equal("Second", list[1].Content);
        Assert.Equal("Third", list[2].Content);
    }

    #endregion

    #region Test Message Classes

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    private class AnotherTestMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    #endregion
}
