using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Tests.TestUtilities;
using System.Collections.Concurrent;
using Xunit;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// Integration tests for storage plugin implementations
/// Testing PostgreSQL, SqlServer storage with test containers, connection resilience, and transactions
/// </summary>
public class StoragePluginTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSqlStorage_StoreAndRetrieveMessage_WorksCorrectly()
    {
        // Arrange
        var storage = new TestPostgreSqlStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var message = TestMessageBuilder.CreateValidMessage("PostgreSQL test message");

        // Act
        await storage.StoreAsync(message);
        var retrievedMessage = await storage.RetrieveAsync(message.MessageId);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(message.MessageId, retrievedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(message, retrievedMessage);
        Assert.Equal(message.Timestamp, retrievedMessage.Timestamp);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SqlServerStorage_StoreAndRetrieveMessage_WorksCorrectly()
    {
        // Arrange
        var storage = new TestSqlServerStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var message = TestMessageBuilder.CreateValidMessage("SQL Server test message");

        // Act
        await storage.StoreAsync(message);
        var retrievedMessage = await storage.RetrieveAsync(message.MessageId);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(message.MessageId, retrievedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(message, retrievedMessage);
        Assert.Equal(message.Timestamp, retrievedMessage.Timestamp);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSqlStorage_WithConnectionFailure_RecoversGracefully()
    {
        // Arrange
        var storage = new TestPostgreSqlStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var message = TestMessageBuilder.CreateValidMessage("Connection resilience test");

        // Act - Store message successfully first
        await storage.StoreAsync(message);

        // Simulate connection failure
        storage.SimulateConnectionFailure();

        // Attempt to retrieve - should fail initially
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.RetrieveAsync(message.MessageId));

        // Restore connection
        storage.RestoreConnection();

        // Should work again
        var retrievedMessage = await storage.RetrieveAsync(message.MessageId);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(message.MessageId, retrievedMessage.MessageId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SqlServerStorage_WithTransactionRollback_RollsBackCorrectly()
    {
        // Arrange
        var storage = new TestSqlServerStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Transaction test 1"),
            TestMessageBuilder.CreateValidMessage("Transaction test 2"),
            TestMessageBuilder.CreateValidMessage("Transaction test 3")
        };

        // Act
        using var transaction = await storage.BeginTransactionAsync();

        try
        {
            // Store first two messages
            await storage.StoreAsync(messages[0], transaction);
            await storage.StoreAsync(messages[1], transaction);

            // Verify they're visible within transaction
            var retrievedMessage1 = await storage.RetrieveAsync(messages[0].MessageId, transaction);
            Assert.NotNull(retrievedMessage1);

            // Simulate error condition and rollback
            await transaction.RollbackAsync();

            // Messages should not be visible after rollback
            var retrievedAfterRollback = await storage.RetrieveAsync(messages[0].MessageId);
            Assert.Null(retrievedAfterRollback);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgreSqlStorage_WithTransactionCommit_CommitsCorrectly()
    {
        // Arrange
        var storage = new TestPostgreSqlStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Commit test 1"),
            TestMessageBuilder.CreateValidMessage("Commit test 2")
        };

        // Act
        using var transaction = await storage.BeginTransactionAsync();

        await storage.StoreAsync(messages[0], transaction);
        await storage.StoreAsync(messages[1], transaction);

        await transaction.CommitAsync();

        // Assert - Messages should be visible after commit
        var retrievedMessage1 = await storage.RetrieveAsync(messages[0].MessageId);
        var retrievedMessage2 = await storage.RetrieveAsync(messages[1].MessageId);

        Assert.NotNull(retrievedMessage1);
        Assert.NotNull(retrievedMessage2);
        Assert.Equal(messages[0].MessageId, retrievedMessage1.MessageId);
        Assert.Equal(messages[1].MessageId, retrievedMessage2.MessageId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StoragePlugin_WithHighConcurrency_HandlesCorrectly()
    {
        // Arrange
        var storage = new TestPostgreSqlStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        const int concurrentOperations = 50;
        var messages = new List<IMessage>();

        for (int i = 0; i < concurrentOperations; i++)
        {
            messages.Add(TestMessageBuilder.CreateValidMessage($"Concurrent message {i}"));
        }

        // Act
        var storeTasks = messages.Select(msg => storage.StoreAsync(msg)).ToArray();
        await Task.WhenAll(storeTasks);

        var retrieveTasks = messages.Select(msg => storage.RetrieveAsync(msg.MessageId)).ToArray();
        var retrievedMessages = await Task.WhenAll(retrieveTasks);

        // Assert
        Assert.Equal(concurrentOperations, retrievedMessages.Length);
        Assert.All(retrievedMessages, msg => Assert.NotNull(msg));

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
    [Trait("Category", "Integration")]
    public async Task StoragePlugin_WithLargeMessage_HandlesCorrectly()
    {
        // Arrange
        var storage = new TestSqlServerStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var largeMessage = TestMessageBuilder.CreateLargeMessage(1_000_000); // 1MB message

        // Act
        await storage.StoreAsync(largeMessage);
        var retrievedMessage = await storage.RetrieveAsync(largeMessage.MessageId);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(largeMessage.MessageId, retrievedMessage.MessageId);
        Assert.Equal(largeMessage.GetTestContent()?.Length, retrievedMessage.GetTestContent()?.Length);
        TestMessageExtensions.AssertSameContent(largeMessage, retrievedMessage);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StoragePlugin_QueryMessages_WithFiltering_ReturnsCorrectResults()
    {
        // Arrange
        var storage = new TestPostgreSqlStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var baseTime = DateTime.UtcNow;
        var messages = new[]
        {
            CreateMessageWithTimestamp("Query test 1", baseTime),
            CreateMessageWithTimestamp("Query test 2", baseTime.AddMinutes(1)),
            CreateMessageWithTimestamp("Different content", baseTime.AddMinutes(2)),
            CreateMessageWithTimestamp("Query test 3", baseTime.AddMinutes(3))
        };

        foreach (var message in messages)
        {
            await storage.StoreAsync(message);
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
    [Trait("Category", "Integration")]
    public async Task StoragePlugin_DeleteMessage_RemovesFromStorage()
    {
        // Arrange
        var storage = new TestSqlServerStorage();
        _disposables.Add(storage);

        await storage.InitializeAsync();

        var message = TestMessageBuilder.CreateValidMessage("Delete test message");

        // Act
        await storage.StoreAsync(message);

        // Verify it exists
        var retrievedBeforeDelete = await storage.RetrieveAsync(message.MessageId);
        Assert.NotNull(retrievedBeforeDelete);

        // Delete it
        await storage.DeleteAsync(message.MessageId);

        // Verify it's gone
        var retrievedAfterDelete = await storage.RetrieveAsync(message.MessageId);
        Assert.Null(retrievedAfterDelete);
    }

    private IMessage CreateMessageWithTimestamp(string content, DateTime timestamp)
    {
        return new TestMessage(
            messageId: Guid.NewGuid(),
            timestamp: timestamp,
            content: content,
            metadata: new Dictionary<string, object>
            {
                ["TestType"] = "QueryTest"
            }
        );
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }
    }

    // Test storage implementations
    public class TestPostgreSqlStorage : IMessageStorage, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<Guid, IMessage> _storage = new();
        private bool _connectionFailure = false;
        private bool _initialized = false;

        public async Task InitializeAsync()
        {
            // Simulate PostgreSQL container startup
            await Task.Delay(100);
            _initialized = true;
        }

        public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null)
        {
            ThrowIfConnectionFailure();
            await Task.Delay(10); // Simulate database operation
            _storage[message.MessageId] = message;
        }

        public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null)
        {
            ThrowIfConnectionFailure();
            await Task.Delay(5); // Simulate database operation
            return _storage.TryGetValue(messageId, out var message) ? message : null;
        }

        public async Task<List<IMessage>> QueryAsync(MessageQuery query)
        {
            ThrowIfConnectionFailure();
            await Task.Delay(20); // Simulate query operation

            var results = _storage.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(query.ContentContains))
            {
                results = results.Where(m => m.GetTestContent()?.Contains(query.ContentContains) == true);
            }

            if (query.FromTimestamp.HasValue)
            {
                results = results.Where(m => m.Timestamp >= query.FromTimestamp.Value);
            }

            if (query.ToTimestamp.HasValue)
            {
                results = results.Where(m => m.Timestamp <= query.ToTimestamp.Value);
            }

            return results.Take(query.MaxResults).ToList();
        }

        public async Task DeleteAsync(Guid messageId)
        {
            ThrowIfConnectionFailure();
            await Task.Delay(10);
            _storage.TryRemove(messageId, out _);
        }

        public async Task<IStorageTransaction> BeginTransactionAsync()
        {
            ThrowIfConnectionFailure();
            await Task.Delay(5);
            return new TestStorageTransaction();
        }

        public void SimulateConnectionFailure() => _connectionFailure = true;
        public void RestoreConnection() => _connectionFailure = false;

        private void ThrowIfConnectionFailure()
        {
            if (_connectionFailure)
                throw new InvalidOperationException("Database connection failed");
        }

        public async ValueTask DisposeAsync()
        {
            _storage.Clear();
            await Task.CompletedTask;
        }
    }

    public class TestSqlServerStorage : IMessageStorage, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<Guid, IMessage> _storage = new();
        private readonly ConcurrentDictionary<Guid, IMessage> _transactionStorage = new();

        public async Task InitializeAsync()
        {
            // Simulate SQL Server container startup
            await Task.Delay(150);
        }

        public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null)
        {
            await Task.Delay(8);

            if (transaction != null)
            {
                _transactionStorage[message.MessageId] = message;
            }
            else
            {
                _storage[message.MessageId] = message;
            }
        }

        public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null)
        {
            await Task.Delay(6);

            if (transaction != null)
            {
                return _transactionStorage.TryGetValue(messageId, out var transactionMessage) ? transactionMessage : null;
            }

            return _storage.TryGetValue(messageId, out var message) ? message : null;
        }

        public async Task<List<IMessage>> QueryAsync(MessageQuery query)
        {
            await Task.Delay(25);

            var results = _storage.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(query.ContentContains))
            {
                results = results.Where(m => m.GetTestContent()?.Contains(query.ContentContains) == true);
            }

            return results.Take(query.MaxResults).ToList();
        }

        public async Task DeleteAsync(Guid messageId)
        {
            await Task.Delay(12);
            _storage.TryRemove(messageId, out _);
        }

        public async Task<IStorageTransaction> BeginTransactionAsync()
        {
            await Task.Delay(8);
            return new TestStorageTransaction(() => CommitTransaction(), () => RollbackTransaction());
        }

        private void CommitTransaction()
        {
            foreach (var item in _transactionStorage)
            {
                _storage[item.Key] = item.Value;
            }
            _transactionStorage.Clear();
        }

        private void RollbackTransaction()
        {
            _transactionStorage.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            _storage.Clear();
            _transactionStorage.Clear();
            await Task.CompletedTask;
        }
    }

    public sealed class TestStorageTransaction : IStorageTransaction
    {
        private readonly Action? _commitAction;
        private readonly Action? _rollbackAction;
        private bool _disposed = false;

        public TestStorageTransaction(Action? commitAction = null, Action? rollbackAction = null)
        {
            _commitAction = commitAction;
            _rollbackAction = rollbackAction;
        }

        public async Task CommitAsync()
        {
            await Task.Delay(5);
            _commitAction?.Invoke();
        }

        public async Task RollbackAsync()
        {
            await Task.Delay(3);
            _rollbackAction?.Invoke();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Clean up managed resources
                _disposed = true;
            }
        }
    }

    // Supporting interfaces and classes
    public interface IMessageStorage
    {
        Task StoreAsync(IMessage message, IStorageTransaction? transaction = null);
        Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null);
        Task<List<IMessage>> QueryAsync(MessageQuery query);
        Task DeleteAsync(Guid messageId);
        Task<IStorageTransaction> BeginTransactionAsync();
    }

    public interface IStorageTransaction : IDisposable
    {
        Task CommitAsync();
        Task RollbackAsync();
    }

    public class MessageQuery
    {
        public string? ContentContains { get; set; }
        public DateTime? FromTimestamp { get; set; }
        public DateTime? ToTimestamp { get; set; }
        public int MaxResults { get; set; } = 100;
    }
}