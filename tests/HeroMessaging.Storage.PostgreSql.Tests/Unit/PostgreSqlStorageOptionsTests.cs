using HeroMessaging.Storage.PostgreSql;
using Xunit;

namespace HeroMessaging.Storage.PostgreSql.Tests.Unit;

public class PostgreSqlStorageOptionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var options = new PostgreSqlStorageOptions();

        // Assert
        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal("public", options.Schema);
        Assert.Equal("messages", options.MessagesTableName);
        Assert.Equal("outbox_messages", options.OutboxTableName);
        Assert.Equal("dead_letter_queue", options.DeadLetterTableName);
        Assert.Equal("sagas", options.SagasTableName);
        Assert.Equal("inbox_messages", options.InboxTableName);
        Assert.Equal("queues", options.QueueTableName);
        Assert.True(options.AutoCreateTables);
        Assert.Equal(30, options.CommandTimeout);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConnectionString_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var connectionString = "Host=localhost;Database=test";

        // Act
        options.ConnectionString = connectionString;

        // Assert
        Assert.Equal(connectionString, options.ConnectionString);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Schema_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var schema = "custom_schema";

        // Act
        options.Schema = schema;

        // Assert
        Assert.Equal(schema, options.Schema);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessagesTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_messages";

        // Act
        options.MessagesTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.MessagesTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OutboxTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_outbox";

        // Act
        options.OutboxTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.OutboxTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeadLetterTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_dlq";

        // Act
        options.DeadLetterTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.DeadLetterTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SagasTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_sagas";

        // Act
        options.SagasTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.SagasTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void InboxTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_inbox";

        // Act
        options.InboxTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.InboxTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void QueueTableName_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var tableName = "custom_queues";

        // Act
        options.QueueTableName = tableName;

        // Assert
        Assert.Equal(tableName, options.QueueTableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AutoCreateTables_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();

        // Act
        options.AutoCreateTables = false;

        // Assert
        Assert.False(options.AutoCreateTables);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CommandTimeout_CanBeSet()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions();
        var timeout = 60;

        // Act
        options.CommandTimeout = timeout;

        // Assert
        Assert.Equal(timeout, options.CommandTimeout);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDefaultSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "public",
            MessagesTableName = "messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("public.messages", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "custom",
            MessagesTableName = "custom_messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("custom.custom_messages", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDifferentTableNames_ReturnsCorrectQualifiedNames()
    {
        // Arrange
        var options = new PostgreSqlStorageOptions
        {
            Schema = "test_schema"
        };

        // Act & Assert
        Assert.Equal("test_schema.messages", options.GetFullTableName(options.MessagesTableName));
        Assert.Equal("test_schema.outbox_messages", options.GetFullTableName(options.OutboxTableName));
        Assert.Equal("test_schema.dead_letter_queue", options.GetFullTableName(options.DeadLetterTableName));
        Assert.Equal("test_schema.sagas", options.GetFullTableName(options.SagasTableName));
        Assert.Equal("test_schema.inbox_messages", options.GetFullTableName(options.InboxTableName));
        Assert.Equal("test_schema.queues", options.GetFullTableName(options.QueueTableName));
    }
}
