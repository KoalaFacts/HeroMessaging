using HeroMessaging.Storage.SqlServer;
using Xunit;

namespace HeroMessaging.Storage.SqlServer.Tests.Unit;

public class SqlServerStorageOptionsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var options = new SqlServerStorageOptions();

        // Assert
        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal("dbo", options.Schema);
        Assert.Equal("Messages", options.MessagesTableName);
        Assert.Equal("OutboxMessages", options.OutboxTableName);
        Assert.Equal("DeadLetterQueue", options.DeadLetterTableName);
        Assert.Equal("Sagas", options.SagasTableName);
        Assert.Equal("InboxMessages", options.InboxTableName);
        Assert.Equal("Queues", options.QueueTableName);
        Assert.True(options.AutoCreateTables);
        Assert.Equal(30, options.CommandTimeout);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConnectionString_CanBeSet()
    {
        // Arrange
        var options = new SqlServerStorageOptions();
        var connectionString = "Server=localhost;Database=test";

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
        var options = new SqlServerStorageOptions();
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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomMessages";

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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomOutbox";

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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomDLQ";

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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomSagas";

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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomInbox";

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
        var options = new SqlServerStorageOptions();
        var tableName = "CustomQueues";

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
        var options = new SqlServerStorageOptions();

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
        var options = new SqlServerStorageOptions();
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
        var options = new SqlServerStorageOptions
        {
            Schema = "dbo",
            MessagesTableName = "Messages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("[dbo].[Messages]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithCustomSchema_ReturnsQualifiedName()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "custom",
            MessagesTableName = "CustomMessages"
        };

        // Act
        var fullName = options.GetFullTableName(options.MessagesTableName);

        // Assert
        Assert.Equal("[custom].[CustomMessages]", fullName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithDifferentTableNames_ReturnsCorrectQualifiedNames()
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = "test_schema"
        };

        // Act & Assert
        Assert.Equal("[test_schema].[Messages]", options.GetFullTableName(options.MessagesTableName));
        Assert.Equal("[test_schema].[OutboxMessages]", options.GetFullTableName(options.OutboxTableName));
        Assert.Equal("[test_schema].[DeadLetterQueue]", options.GetFullTableName(options.DeadLetterTableName));
        Assert.Equal("[test_schema].[Sagas]", options.GetFullTableName(options.SagasTableName));
        Assert.Equal("[test_schema].[InboxMessages]", options.GetFullTableName(options.InboxTableName));
        Assert.Equal("[test_schema].[Queues]", options.GetFullTableName(options.QueueTableName));
    }

    [Theory]
    [InlineData("dbo", "TestTable", "[dbo].[TestTable]")]
    [InlineData("schema1", "Table1", "[schema1].[Table1]")]
    [InlineData("MySchema", "MyTable", "[MySchema].[MyTable]")]
    [Trait("Category", "Unit")]
    public void GetFullTableName_WithVariousSchemaAndTableNames_ReturnsCorrectQualifiedName(
        string schema, string tableName, string expected)
    {
        // Arrange
        var options = new SqlServerStorageOptions
        {
            Schema = schema
        };

        // Act
        var fullName = options.GetFullTableName(tableName);

        // Assert
        Assert.Equal(expected, fullName);
    }
}
