using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Core.Storage;
using HeroMessaging.Tests.Unit.Fixtures;

namespace HeroMessaging.Tests.Unit.Storage;

public class InMemoryMessageStorageTests
{
    private readonly InMemoryMessageStorage _storage;

    public InMemoryMessageStorageTests()
    {
        _storage = new InMemoryMessageStorage();
    }

    [Fact]
    public async Task Store_ShouldAddMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };

        // Act
        var id = await _storage.Store(message);

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(id);
        var retrieved = await _storage.Retrieve<TestMessage>(id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Content);
    }

    [Fact]
    public async Task Retrieve_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _storage.Retrieve<TestMessage>("invalid-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Retrieve_WithWrongType_ShouldReturnNull()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        var id = await _storage.Store(message);

        // Act
        var result = await _storage.Retrieve<DifferentMessage>(id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_ShouldRemoveMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        var id = await _storage.Store(message);

        // Act
        var deleted = await _storage.Delete(id);
        var retrieved = await _storage.Retrieve<TestMessage>(id);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Delete_WithInvalidId_ShouldReturnFalse()
    {
        // Act
        var deleted = await _storage.Delete("invalid-id");

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task Query_ShouldReturnAllMessages()
    {
        // Arrange
        var messages = new[]
        {
            new TestMessage { Content = "Alpha", Priority = 1 },
            new TestMessage { Content = "Beta", Priority = 2 },
            new TestMessage { Content = "Gamma", Priority = 1 }
        };

        foreach (var msg in messages)
        {
            await _storage.Store(msg);
        }

        // Act
        var query = new MessageQuery { Limit = 10 };
        var result = await _storage.Query<TestMessage>(query);

        // Assert
        Assert.Equal(3, result.Count());
    }

    [Fact]
    public async Task Update_ShouldModifyExistingMessage()
    {
        // Arrange
        var message = new TestMessage { Content = "Original" };
        var id = await _storage.Store(message);
        message.Content = "Updated";

        // Act
        var updated = await _storage.Update(id, message);
        var retrieved = await _storage.Retrieve<TestMessage>(id);

        // Assert
        Assert.True(updated);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.Content);
    }

    [Fact]
    public async Task Update_WithInvalidId_ShouldReturnFalse()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };

        // Act
        var updated = await _storage.Update("invalid-id", message);

        // Assert
        Assert.False(updated);
    }

    [Fact]
    public async Task Count_ShouldReturnCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _storage.Store(new TestMessage { Content = $"Message {i}" });
        }

        // Act
        var count = await _storage.Count();

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Exists_ShouldReturnCorrectResult()
    {
        // Arrange
        var message = new TestMessage { Content = "Test" };
        var id = await _storage.Store(message);

        // Act
        var exists = await _storage.Exists(id);
        var notExists = await _storage.Exists("invalid-id");

        // Assert
        Assert.True(exists);
        Assert.False(notExists);
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllMessages()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _storage.Store(new TestMessage { Content = $"Message {i}" });
        }

        // Act
        await _storage.Clear();
        var count = await _storage.Count();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Store_WithOptions_ShouldRespectTtl()
    {
        // Arrange
        var message = new TestMessage { Content = "Expiring" };
        var options = new MessageStorageOptions { Ttl = TimeSpan.FromMilliseconds(1) };

        // Act
        var id = await _storage.Store(message, options);
        await Task.Delay(10); // Wait for expiration
        var retrieved = await _storage.Retrieve<TestMessage>(id);

        // Assert
        Assert.Null(retrieved); // Should be expired and removed
    }
}