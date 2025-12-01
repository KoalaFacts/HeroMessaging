using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Tests.TestUtilities;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Integration;

[Trait("Category", "Integration")]
public class SqlServerStorageIntegrationTests : SqlServerIntegrationTestBase
{
    [Fact]
    public async Task SqlServerStorage_StoreAndRetrieveMessage_WorksCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var message = TestMessageBuilder.CreateValidMessage("SQL Server test message");

        // Act
        await storage.StoreAsync(message, (MessageStorageOptions?)null, TestContext.Current.CancellationToken);
        var retrievedMessage = await storage.RetrieveAsync(message.MessageId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(message.MessageId, retrievedMessage.MessageId);
        TestMessageExtensions.AssertSameContent(message, retrievedMessage);
        Assert.Equal(message.Timestamp, retrievedMessage.Timestamp);
    }

    [Fact]
    public async Task SqlServerStorage_WithTransactionRollback_RollsBackCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Transaction test 1"),
            TestMessageBuilder.CreateValidMessage("Transaction test 2"),
            TestMessageBuilder.CreateValidMessage("Transaction test 3")
        };

        // Act
        using var transaction = await storage.BeginTransactionAsync(TestContext.Current.CancellationToken);

        try
        {
            // Store first two messages
            await storage.StoreAsync(messages[0], transaction, TestContext.Current.CancellationToken);
            await storage.StoreAsync(messages[1], transaction, TestContext.Current.CancellationToken);

            // Verify they're visible within transaction
            var retrievedMessage1 = await storage.RetrieveAsync(messages[0].MessageId, transaction, TestContext.Current.CancellationToken);
            Assert.NotNull(retrievedMessage1);

            // Simulate error condition and rollback
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);

            // Messages should not be visible after rollback
            var retrievedAfterRollback = await storage.RetrieveAsync(messages[0].MessageId, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(retrievedAfterRollback);
        }
        catch
        {
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
            throw;
        }
    }

    [Fact]
    public async Task SqlServerStorage_WithLargeMessage_HandlesCorrectly()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var largeMessage = TestMessageBuilder.CreateLargeMessage(1_000_000); // 1MB message

        // Act
        await storage.StoreAsync(largeMessage, (MessageStorageOptions?)null, TestContext.Current.CancellationToken);
        var retrievedMessage = await storage.RetrieveAsync(largeMessage.MessageId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrievedMessage);
        Assert.Equal(largeMessage.MessageId, retrievedMessage.MessageId);
        Assert.Equal(largeMessage.GetTestContent()?.Length, retrievedMessage.GetTestContent()?.Length);
        TestMessageExtensions.AssertSameContent(largeMessage, retrievedMessage);
    }

    [Fact]
    public async Task SqlServerStorage_DeleteMessage_RemovesFromStorage()
    {
        // Arrange
        var storage = CreateMessageStorage();
        var message = TestMessageBuilder.CreateValidMessage("Delete test message");

        // Act
        await storage.StoreAsync(message, (MessageStorageOptions?)null, TestContext.Current.CancellationToken);

        // Verify it exists
        var retrievedBeforeDelete = await storage.RetrieveAsync(message.MessageId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedBeforeDelete);

        // Delete it
        await storage.DeleteAsync(message.MessageId, TestContext.Current.CancellationToken);

        // Verify it's gone
        var retrievedAfterDelete = await storage.RetrieveAsync(message.MessageId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(retrievedAfterDelete);
    }
}
