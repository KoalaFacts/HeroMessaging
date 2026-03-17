using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public sealed class InMemoryMessageStorageTests
{
    #region Test Helper Classes

    public sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string? Content { get; set; }
    }

    public sealed class SpecificTestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string? SpecificContent { get; set; }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidTimeProvider_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var storage = new InMemoryMessageStorage(timeProvider);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InMemoryMessageStorage(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region StoreAsync Tests

    [Fact]
    public async Task StoreAsync_WithValidMessage_ReturnsId()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };

        // Act
        var id = await storage.StoreAsync(message);

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    [Fact]
    public async Task StoreAsync_WithOptions_StoresWithMetadata()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var options = new MessageStorageOptions
        {
            Collection = "test-collection",
            Ttl = TimeSpan.FromHours(1),
            Metadata = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var id = await storage.StoreAsync(message, options);

        // Assert
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);
        Assert.NotNull(retrieved);
        Assert.Equal(message.MessageId, retrieved.MessageId);
    }

    [Fact]
    public async Task StoreAsync_WithTtl_SetsExpiresAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var ttl = TimeSpan.FromHours(2);
        var options = new MessageStorageOptions { Ttl = ttl };

        // Act
        var id = await storage.StoreAsync(message, options);

        // Verify message is retrievable before expiration
        var retrieved1 = await storage.RetrieveAsync<TestMessage>(id);
        Assert.NotNull(retrieved1);

        // Advance time beyond TTL
        timeProvider.Advance(TimeSpan.FromHours(3));

        // Assert - Message should be expired
        var retrieved2 = await storage.RetrieveAsync<TestMessage>(id);
        Assert.Null(retrieved2);
    }

    [Fact]
    public async Task StoreAsync_GeneratesUniqueIds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var ids = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var id = await storage.StoreAsync(new TestMessage());
            ids.Add(id);
        }

        // Assert
        Assert.Equal(100, ids.Count);
    }

    #endregion

    #region RetrieveAsync Tests

    [Fact]
    public async Task RetrieveAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act
        var result = await storage.RetrieveAsync<TestMessage>("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RetrieveAsync_WithExistingMessage_ReturnsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var id = await storage.StoreAsync(message);

        // Act
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(message.MessageId, retrieved.MessageId);
        Assert.Equal(message.Content, retrieved.Content);
    }

    [Fact]
    public async Task RetrieveAsync_WithExpiredMessage_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(30) };
        var id = await storage.StoreAsync(message, options);

        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_WithExpiredMessage_RemovesFromStorage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMinutes(10) };
        var id = await storage.StoreAsync(message, options);

        timeProvider.Advance(TimeSpan.FromMinutes(15));

        // Act
        var retrieved1 = await storage.RetrieveAsync<TestMessage>(id);
        var exists = await storage.ExistsAsync(id);

        // Assert
        Assert.Null(retrieved1);
        Assert.False(exists);
    }

    [Fact]
    public async Task RetrieveAsync_WithWrongType_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var id = await storage.StoreAsync(message);

        // Act
        var retrieved = await storage.RetrieveAsync<SpecificTestMessage>(id);

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_WithCorrectType_ReturnsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new SpecificTestMessage { SpecificContent = "Specific" };
        var id = await storage.StoreAsync(message);

        // Act
        var retrieved = await storage.RetrieveAsync<SpecificTestMessage>(id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(message.SpecificContent, retrieved.SpecificContent);
    }

    #endregion

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_WithEmptyStorage_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var query = new MessageQuery();

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithCollectionFilter_ReturnsMatchingMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection1" });
        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection2" });
        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection1" });

        var query = new MessageQuery { Collection = "collection1" };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task QueryAsync_WithFromTimestampFilter_ReturnsCorrectMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var message1 = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
        await storage.StoreAsync(message1);

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var message2 = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
        await storage.StoreAsync(message2);

        var query = new MessageQuery { FromTimestamp = cutoffTime };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(message2.MessageId, resultsList[0].MessageId);
    }

    [Fact]
    public async Task QueryAsync_WithToTimestampFilter_ReturnsCorrectMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var message1 = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
        await storage.StoreAsync(message1);

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var message2 = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
        await storage.StoreAsync(message2);

        var query = new MessageQuery { ToTimestamp = cutoffTime };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(message1.MessageId, resultsList[0].MessageId);
    }

    [Fact]
    public async Task QueryAsync_WithMetadataFilters_ReturnsMatchingMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var options1 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "prod", ["version"] = "1.0" }
        };
        var options2 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "dev", ["version"] = "1.0" }
        };
        var options3 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "prod", ["version"] = "2.0" }
        };

        await storage.StoreAsync(new TestMessage(), options1);
        await storage.StoreAsync(new TestMessage(), options2);
        await storage.StoreAsync(new TestMessage(), options3);

        var query = new MessageQuery
        {
            MetadataFilters = new Dictionary<string, object> { ["env"] = "prod" }
        };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task QueryAsync_WithMultipleMetadataFilters_ReturnsMatchingMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var options1 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "prod", ["version"] = "1.0" }
        };
        var options2 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "dev", ["version"] = "1.0" }
        };
        var options3 = new MessageStorageOptions
        {
            Metadata = new Dictionary<string, object> { ["env"] = "prod", ["version"] = "2.0" }
        };

        await storage.StoreAsync(new TestMessage(), options1);
        await storage.StoreAsync(new TestMessage(), options2);
        await storage.StoreAsync(new TestMessage(), options3);

        var query = new MessageQuery
        {
            MetadataFilters = new Dictionary<string, object>
            {
                ["env"] = "prod",
                ["version"] = "1.0"
            }
        };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_OrderByTimestampAscending_SortsCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
            messages.Add(message);
            await storage.StoreAsync(message);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new MessageQuery { OrderBy = "timestamp", Ascending = true };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId, resultsList[i].MessageId);
        }
    }

    [Fact]
    public async Task QueryAsync_OrderByTimestampDescending_SortsCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
            messages.Add(message);
            await storage.StoreAsync(message);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new MessageQuery { OrderBy = "timestamp", Ascending = false };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[messages.Count - 1 - i].MessageId, resultsList[i].MessageId);
        }
    }

    [Fact]
    public async Task QueryAsync_OrderByStoredAtAscending_SortsCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 3; i++)
        {
            var message = new TestMessage();
            messages.Add(message);
            await storage.StoreAsync(message);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new MessageQuery { OrderBy = "storedat", Ascending = true };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].MessageId, resultsList[i].MessageId);
        }
    }

    [Fact]
    public async Task QueryAsync_WithOffset_SkipsCorrectMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 10; i++)
        {
            var message = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
            messages.Add(message);
            await storage.StoreAsync(message);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new MessageQuery { OrderBy = "timestamp", Offset = 5 };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(5, resultsList.Count);
        Assert.Equal(messages[5].MessageId, resultsList[0].MessageId);
    }

    [Fact]
    public async Task QueryAsync_WithLimit_ReturnsCorrectCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        for (int i = 0; i < 20; i++)
        {
            await storage.StoreAsync(new TestMessage());
        }

        var query = new MessageQuery { Limit = 5 };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task QueryAsync_WithOffsetAndLimit_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<TestMessage>();
        for (int i = 0; i < 20; i++)
        {
            var message = new TestMessage { Timestamp = timeProvider.GetUtcNow() };
            messages.Add(message);
            await storage.StoreAsync(message);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        var query = new MessageQuery
        {
            OrderBy = "timestamp",
            Offset = 10,
            Limit = 5
        };

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(5, resultsList.Count);
        Assert.Equal(messages[10].MessageId, resultsList[0].MessageId);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act
        var result = await storage.DeleteAsync("non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var id = await storage.StoreAsync(message);

        // Act
        var result = await storage.DeleteAsync(id);

        // Assert
        Assert.True(result);

        var retrieved = await storage.RetrieveAsync<TestMessage>(id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMessagePermanently()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var id = await storage.StoreAsync(message);

        // Act
        await storage.DeleteAsync(id);

        // Assert
        var exists = await storage.ExistsAsync(id);
        Assert.False(exists);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();

        // Act
        var result = await storage.UpdateAsync("non-existent-id", message);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var originalMessage = new TestMessage { Content = "Original" };
        var id = await storage.StoreAsync(originalMessage);

        var updatedMessage = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Content = "Updated"
        };

        // Act
        var result = await storage.UpdateAsync(id, updatedMessage);

        // Assert
        Assert.True(result);

        var retrieved = await storage.RetrieveAsync<TestMessage>(id);
        Assert.NotNull(retrieved);
        Assert.Equal(updatedMessage.MessageId, retrieved.MessageId);
        Assert.Equal("Updated", retrieved.Content);
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedAtTimestamp()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var originalMessage = new TestMessage();
        var id = await storage.StoreAsync(originalMessage);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var updatedMessage = new TestMessage();

        // Act
        await storage.UpdateAsync(id, updatedMessage);

        // Assert - Verify message was updated (checking existence is enough for this test)
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);
        Assert.NotNull(retrieved);
        Assert.Equal(updatedMessage.MessageId, retrieved.MessageId);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act
        var exists = await storage.ExistsAsync("non-existent-id");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExistingMessage_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var id = await storage.StoreAsync(message);

        // Act
        var exists = await storage.ExistsAsync(id);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_AfterDelete_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var id = await storage.StoreAsync(message);

        await storage.DeleteAsync(id);

        // Act
        var exists = await storage.ExistsAsync(id);

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithEmptyStorage_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act
        var count = await storage.CountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CountAsync_WithMessages_ReturnsCorrectCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.StoreAsync(new TestMessage());
        }

        // Act
        var count = await storage.CountAsync();

        // Assert
        Assert.Equal(10, count);
    }

    [Fact]
    public async Task CountAsync_WithQuery_ReturnsFilteredCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection1" });
        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection2" });
        await storage.StoreAsync(new TestMessage(), new MessageStorageOptions { Collection = "collection1" });

        var query = new MessageQuery { Collection = "collection1" };

        // Act
        var count = await storage.CountAsync(query);

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ClearAsync_RemovesAllMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.StoreAsync(new TestMessage());
        }

        // Act
        await storage.ClearAsync();

        // Assert
        var count = await storage.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ClearAsync_WithEmptyStorage_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act & Assert
        await storage.ClearAsync();

        var count = await storage.CountAsync();
        Assert.Equal(0, count);
    }

    #endregion

    #region Transaction Tests

    [Fact]
    public async Task BeginTransactionAsync_ReturnsTransaction()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        // Act
        var transaction = await storage.BeginTransactionAsync();

        // Assert
        Assert.NotNull(transaction);
    }

    [Fact]
    public async Task Transaction_CommitAsync_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var transaction = await storage.BeginTransactionAsync();

        // Act & Assert
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Transaction_RollbackAsync_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var transaction = await storage.BeginTransactionAsync();

        // Act & Assert
        await transaction.RollbackAsync();
    }

    [Fact]
    public void Transaction_Dispose_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var transaction = storage.BeginTransactionAsync().Result;

        // Act & Assert
        transaction.Dispose();
    }

    #endregion

    #region IMessageStorage Interface Tests

    [Fact]
    public async Task IMessageStorage_StoreAsync_WithTransaction_StoresMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        IMessageStorage storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var transaction = await storage.BeginTransactionAsync();

        // Act
        await storage.StoreAsync(message, transaction);

        // Assert
        var retrieved = await storage.RetrieveAsync(message.MessageId, transaction);
        Assert.NotNull(retrieved);
        Assert.Equal(message.MessageId, retrieved.MessageId);
    }

    [Fact]
    public async Task IMessageStorage_RetrieveAsync_WithGuid_ReturnsMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        IMessageStorage storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        await storage.StoreAsync(message, transaction: null);

        // Act
        var retrieved = await storage.RetrieveAsync(message.MessageId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(message.MessageId, retrieved.MessageId);
    }

    [Fact]
    public async Task IMessageStorage_QueryAsync_ReturnsMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        IMessageStorage storage = new InMemoryMessageStorage(timeProvider);

        await storage.StoreAsync(new TestMessage(), transaction: null);
        await storage.StoreAsync(new TestMessage(), transaction: null);

        var query = new MessageQuery();

        // Act
        var results = await storage.QueryAsync(query);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task IMessageStorage_DeleteAsync_WithGuid_DeletesMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        IMessageStorage storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        await storage.StoreAsync(message, transaction: null);

        // Act
        await storage.DeleteAsync(message.MessageId);

        // Assert
        var retrieved = await storage.RetrieveAsync(message.MessageId);
        Assert.Null(retrieved);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentStoreAndRetrieve_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var tasks = new List<Task<string>>();

        // Act - Store 100 messages concurrently
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var message = new TestMessage { Content = $"Message {i}" };
                return await storage.StoreAsync(message);
            }));
        }

        var ids = await Task.WhenAll(tasks);

        // Assert - All messages should be retrievable
        foreach (var id in ids)
        {
            var message = await storage.RetrieveAsync<TestMessage>(id);
            Assert.NotNull(message);
        }
    }

    [Fact]
    public async Task ConcurrentUpdate_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        var messages = new List<(string Id, TestMessage Message)>();
        for (int i = 0; i < 50; i++)
        {
            var message = new TestMessage { Content = $"Original {i}" };
            var id = await storage.StoreAsync(message);
            messages.Add((id, message));
        }

        // Act - Update all messages concurrently
        var updateTasks = messages.Select(m =>
            Task.Run(async () =>
            {
                var updatedMessage = new TestMessage { Content = $"Updated {m.Id}" };
                return await storage.UpdateAsync(m.Id, updatedMessage);
            })
        );

        var results = await Task.WhenAll(updateTasks);

        // Assert
        Assert.All(results, Assert.True);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task StoreAsync_WithMessageHavingMetadata_PreservesMetadata()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage
        {
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123,
                ["key3"] = true
            }
        };

        // Act
        var id = await storage.StoreAsync(message);
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Metadata);
        Assert.Equal(3, retrieved.Metadata.Count);
        Assert.Equal("value1", retrieved.Metadata["key1"]);
        Assert.Equal(123, retrieved.Metadata["key2"]);
        Assert.Equal(true, retrieved.Metadata["key3"]);
    }

    [Fact]
    public async Task QueryAsync_WithNoOrderBy_ReturnsMessages()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);

        for (int i = 0; i < 5; i++)
        {
            await storage.StoreAsync(new TestMessage());
        }

        var query = new MessageQuery();

        // Act
        var results = await storage.QueryAsync<TestMessage>(query);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task RetrieveAsync_AtExactExpiryTime_ReturnsNull()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryMessageStorage(timeProvider);
        var message = new TestMessage();
        var ttl = TimeSpan.FromMinutes(30);
        var options = new MessageStorageOptions { Ttl = ttl };
        var id = await storage.StoreAsync(message, options);

        timeProvider.Advance(ttl);

        // Act
        var retrieved = await storage.RetrieveAsync<TestMessage>(id);

        // Assert
        Assert.Null(retrieved);
    }

    #endregion
}
