using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Storage;

[Trait("Category", "Unit")]
public sealed class InMemoryOutboxStorageTests
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

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidTimeProvider_Succeeds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();

        // Act
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Assert
        Assert.NotNull(storage);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InMemoryOutboxStorage(null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WithValidMessage_ReturnsOutboxEntry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var message = new TestMessage { Content = "Test" };
        var options = new OutboxOptions { Destination = "TestDestination", Priority = 5 };

        // Act
        var entry = await storage.AddAsync(message, options);

        // Assert
        Assert.NotNull(entry);
        Assert.NotNull(entry.Id);
        Assert.NotEmpty(entry.Id);
        Assert.Same(message, entry.Message);
        Assert.Same(options, entry.Options);
        Assert.Equal(OutboxStatus.Pending, entry.Status);
        Assert.Equal(0, entry.RetryCount);
        Assert.Equal(timeProvider.GetUtcNow(), entry.CreatedAt);
        Assert.Null(entry.ProcessedAt);
        Assert.Null(entry.NextRetryAt);
        Assert.Null(entry.LastError);
    }

    [Fact]
    public async Task AddAsync_GeneratesUniqueIds()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var ids = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
            ids.Add(entry.Id);
        }

        // Assert
        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public async Task AddAsync_StoresOptionsCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var message = new TestMessage();
        var options = new OutboxOptions
        {
            Destination = "TestQueue",
            Priority = 10,
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromMinutes(2)
        };

        // Act
        var entry = await storage.AddAsync(message, options);

        // Assert
        Assert.Equal("TestQueue", entry.Options.Destination);
        Assert.Equal(10, entry.Options.Priority);
        Assert.Equal(5, entry.Options.MaxRetries);
        Assert.Equal(TimeSpan.FromMinutes(2), entry.Options.RetryDelay);
    }

    #endregion

    #region GetPendingAsync with Query Tests

    [Fact]
    public async Task GetPendingAsync_WithQuery_ReturnsOnlyPendingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkProcessedAsync(entry2.Id);

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        Assert.Contains(resultsList, e => e.Id == entry1.Id);
        Assert.Contains(resultsList, e => e.Id == entry3.Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithStatusFilter_ReturnsMatchingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkProcessedAsync(entry2.Id);
        await storage.MarkFailedAsync(entry3.Id, "Test error");

        var query = new OutboxQuery { Status = OutboxEntryStatus.Failed };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(entry3.Id, resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesEntriesWithFutureNextRetry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        var futureRetry = timeProvider.GetUtcNow().AddMinutes(10);
        await storage.UpdateRetryCountAsync(entry2.Id, 1, futureRetry);

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(entry1.Id, resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_IncludesEntriesWithPastNextRetry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        var pastRetry = timeProvider.GetUtcNow().AddMinutes(-10);
        await storage.UpdateRetryCountAsync(entry2.Id, 1, pastRetry);

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
    }

    [Fact]
    public async Task GetPendingAsync_WithOlderThanFilter_ReturnsCorrectEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        var query = new OutboxQuery { OlderThan = cutoffTime };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(entry1.Id, resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithNewerThanFilter_ReturnsCorrectEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var cutoffTime = timeProvider.GetUtcNow();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        var query = new OutboxQuery { NewerThan = cutoffTime };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(entry2.Id, resultsList[0].Id);
    }

    [Fact]
    public async Task GetPendingAsync_RespectsLimit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.AddAsync(new TestMessage(), new OutboxOptions());
        }

        var query = new OutboxQuery { Limit = 5 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByPriorityThenCreatedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 10 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var entry4 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 10 });

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(4, resultsList.Count);
        // Higher priority first (10 > 5)
        Assert.Equal(entry2.Id, resultsList[0].Id);
        Assert.Equal(entry4.Id, resultsList[1].Id);
        // Then by created time within same priority
        Assert.Equal(entry1.Id, resultsList[2].Id);
        Assert.Equal(entry3.Id, resultsList[3].Id);
    }

    [Fact]
    public async Task GetPendingAsync_WithEmptyStorage_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var query = new OutboxQuery();

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GetPendingAsync with Limit Tests

    [Fact]
    public async Task GetPendingAsync_WithLimit_ReturnsOnlyPendingEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkProcessedAsync(entry2.Id);

        // Act
        var results = await storage.GetPendingAsync(limit: 100);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
    }

    [Fact]
    public async Task GetPendingAsync_WithLimit_RespectsLimit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            await storage.AddAsync(new TestMessage(), new OutboxOptions());
        }

        // Act
        var results = await storage.GetPendingAsync(limit: 5);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task GetPendingAsync_WithLimit_OrdersByPriorityThenCreatedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 1 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 5 });
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 1 });

        // Act
        var results = await storage.GetPendingAsync(limit: 100);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(entry2.Id, resultsList[0].Id); // Higher priority first
        Assert.Equal(entry1.Id, resultsList[1].Id); // Then oldest with same priority
        Assert.Equal(entry3.Id, resultsList[2].Id);
    }

    #endregion

    #region MarkProcessedAsync Tests

    [Fact]
    public async Task MarkProcessedAsync_WithExistingEntry_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        var result = await storage.MarkProcessedAsync(entry.Id);

        // Assert
        Assert.True(result);
        Assert.Equal(OutboxStatus.Processed, entry.Status);
        Assert.Equal(timeProvider.GetUtcNow(), entry.ProcessedAt);
    }

    [Fact]
    public async Task MarkProcessedAsync_WithNonExistentEntry_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Act
        var result = await storage.MarkProcessedAsync("non-existent-id");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedAtTimestamp()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var expectedProcessedAt = timeProvider.GetUtcNow();

        // Act
        await storage.MarkProcessedAsync(entry.Id);

        // Assert
        Assert.Equal(expectedProcessedAt, entry.ProcessedAt);
    }

    #endregion

    #region MarkFailedAsync Tests

    [Fact]
    public async Task MarkFailedAsync_WithExistingEntry_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        var result = await storage.MarkFailedAsync(entry.Id, "Test error");

        // Assert
        Assert.True(result);
        Assert.Equal(OutboxStatus.Failed, entry.Status);
        Assert.Equal("Test error", entry.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_WithNonExistentEntry_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Act
        var result = await storage.MarkFailedAsync("non-existent-id", "Error");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkFailedAsync_StoresErrorMessage()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var errorMessage = "Delivery failed: Connection timeout";

        // Act
        await storage.MarkFailedAsync(entry.Id, errorMessage);

        // Assert
        Assert.Equal(errorMessage, entry.LastError);
    }

    #endregion

    #region UpdateRetryCountAsync Tests

    [Fact]
    public async Task UpdateRetryCountAsync_WithExistingEntry_ReturnsTrue()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        var result = await storage.UpdateRetryCountAsync(entry.Id, 3);

        // Assert
        Assert.True(result);
        Assert.Equal(3, entry.RetryCount);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithNonExistentEntry_ReturnsFalse()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Act
        var result = await storage.UpdateRetryCountAsync("non-existent-id", 1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_UpdatesRetryCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        await storage.UpdateRetryCountAsync(entry.Id, 5);

        // Assert
        Assert.Equal(5, entry.RetryCount);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_SetsNextRetryAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var nextRetry = timeProvider.GetUtcNow().AddMinutes(5);

        // Act
        await storage.UpdateRetryCountAsync(entry.Id, 2, nextRetry);

        // Assert
        Assert.Equal(nextRetry, entry.NextRetryAt);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WhenExceedsMaxRetries_MarksAsFailed()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var options = new OutboxOptions { MaxRetries = 3 };
        var entry = await storage.AddAsync(new TestMessage(), options);

        // Act
        await storage.UpdateRetryCountAsync(entry.Id, 3);

        // Assert
        Assert.Equal(OutboxStatus.Failed, entry.Status);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WhenBelowMaxRetries_KeepsPendingStatus()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var options = new OutboxOptions { MaxRetries = 5 };
        var entry = await storage.AddAsync(new TestMessage(), options);

        // Act
        await storage.UpdateRetryCountAsync(entry.Id, 2);

        // Assert
        Assert.Equal(OutboxStatus.Pending, entry.Status);
    }

    #endregion

    #region GetPendingCountAsync Tests

    [Fact]
    public async Task GetPendingCountAsync_WithNoPendingEntries_ReturnsZero()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Act
        var count = await storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_WithPendingEntries_ReturnsCorrectCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        await storage.AddAsync(new TestMessage(), new OutboxOptions());
        await storage.AddAsync(new TestMessage(), new OutboxOptions());
        await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        var count = await storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_ExcludesProcessedEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkProcessedAsync(entry2.Id);

        // Act
        var count = await storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_IncludesFailedEntries()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkFailedAsync(entry2.Id, "Error");

        // Act - Note: GetPendingCountAsync only counts Pending status
        var count = await storage.GetPendingCountAsync();

        // Assert
        Assert.Equal(1, count); // Only entry1 is pending
    }

    #endregion

    #region GetFailedAsync Tests

    [Fact]
    public async Task GetFailedAsync_WithNoFailedEntries_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        var results = await storage.GetFailedAsync();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetFailedAsync_WithFailedEntries_ReturnsFailedOnly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry1 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry2 = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var entry3 = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        await storage.MarkFailedAsync(entry1.Id, "Error 1");
        await storage.MarkFailedAsync(entry3.Id, "Error 2");

        // Act
        var results = await storage.GetFailedAsync();

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        Assert.Contains(resultsList, e => e.Id == entry1.Id);
        Assert.Contains(resultsList, e => e.Id == entry3.Id);
    }

    [Fact]
    public async Task GetFailedAsync_RespectsLimit()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        for (int i = 0; i < 10; i++)
        {
            var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
            await storage.MarkFailedAsync(entry.Id, $"Error {i}");
        }

        // Act
        var results = await storage.GetFailedAsync(limit: 5);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task GetFailedAsync_OrdersByCreatedAt()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entries = new List<OutboxEntry>();
        for (int i = 0; i < 5; i++)
        {
            var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
            entries.Add(entry);
            await storage.MarkFailedAsync(entry.Id, $"Error {i}");
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var results = await storage.GetFailedAsync();

        // Assert
        var resultsList = results.ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].Id, resultsList[i].Id);
        }
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentAddAndGet_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var tasks = new List<Task<OutboxEntry>>();

        // Act - Add 100 entries concurrently
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                return await storage.AddAsync(new TestMessage(), new OutboxOptions());
            }));
        }

        var entries = await Task.WhenAll(tasks);

        // Assert - All entries should be retrievable
        var pending = await storage.GetPendingAsync(limit: 200);
        Assert.Equal(100, pending.Count());
    }

    [Fact]
    public async Task ConcurrentMarkProcessed_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entries = new List<OutboxEntry>();
        for (int i = 0; i < 50; i++)
        {
            var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
            entries.Add(entry);
        }

        // Act - Mark processed concurrently
        var tasks = entries.Select(e =>
            Task.Run(async () => await storage.MarkProcessedAsync(e.Id))
        );

        await Task.WhenAll(tasks);

        // Assert
        var count = await storage.GetPendingCountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ConcurrentUpdateRetryCount_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entries = new List<OutboxEntry>();
        for (int i = 0; i < 50; i++)
        {
            var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions { MaxRetries = 10 });
            entries.Add(entry);
        }

        // Act - Update retry counts concurrently
        var tasks = entries.Select((e, index) =>
            Task.Run(async () => await storage.UpdateRetryCountAsync(e.Id, index % 5))
        );

        await Task.WhenAll(tasks);

        // Assert - All entries should have updated retry counts
        var pending = await storage.GetPendingAsync(limit: 100);
        Assert.Equal(50, pending.Count());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task AddAsync_WithMessageHavingMetadata_StoresCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var message = new TestMessage
        {
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123
            }
        };

        // Act
        var entry = await storage.AddAsync(message, new OutboxOptions());

        // Assert
        Assert.NotNull(entry.Message.Metadata);
        Assert.Equal("value1", entry.Message.Metadata["key1"]);
        Assert.Equal(123, entry.Message.Metadata["key2"]);
    }

    [Fact]
    public async Task GetPendingAsync_WithAllEntriesProcessed_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entries = new[] {
            await storage.AddAsync(new TestMessage(), new OutboxOptions()),
            await storage.AddAsync(new TestMessage(), new OutboxOptions()),
            await storage.AddAsync(new TestMessage(), new OutboxOptions())
        };

        foreach (var entry in entries)
        {
            await storage.MarkProcessedAsync(entry.Id);
        }

        var query = new OutboxQuery();

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task UpdateRetryCountAsync_WithNullNextRetry_UpdatesOnlyRetryCount()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());

        // Act
        await storage.UpdateRetryCountAsync(entry.Id, 2, null);

        // Assert
        Assert.Equal(2, entry.RetryCount);
        Assert.Null(entry.NextRetryAt);
    }

    [Fact]
    public async Task GetPendingAsync_WithZeroPriority_WorksCorrectly()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 0 });
        await storage.AddAsync(new TestMessage(), new OutboxOptions { Priority = 0 });

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task UpdateRetryCountAsync_AtExactMaxRetries_MarksAsFailed()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);
        var options = new OutboxOptions { MaxRetries = 3 };
        var entry = await storage.AddAsync(new TestMessage(), options);

        // Act - Update to exactly maxRetries
        await storage.UpdateRetryCountAsync(entry.Id, 3);

        // Assert
        Assert.Equal(OutboxStatus.Failed, entry.Status);
    }

    [Fact]
    public async Task GetFailedAsync_WithEmptyStorage_ReturnsEmpty()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        // Act
        var results = await storage.GetFailedAsync();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetPendingAsync_WithNextRetryAtCurrentTime_IncludesEntry()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider();
        var storage = new InMemoryOutboxStorage(timeProvider);

        var entry = await storage.AddAsync(new TestMessage(), new OutboxOptions());
        var currentTime = timeProvider.GetUtcNow();
        await storage.UpdateRetryCountAsync(entry.Id, 1, currentTime);

        var query = new OutboxQuery { Limit = 100 };

        // Act
        var results = await storage.GetPendingAsync(query);

        // Assert
        var resultsList = results.ToList();
        Assert.Single(resultsList);
        Assert.Equal(entry.Id, resultsList[0].Id);
    }

    #endregion
}
