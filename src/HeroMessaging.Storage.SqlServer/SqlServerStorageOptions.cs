namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// Configuration options for SQL Server storage providers
/// </summary>
public class SqlServerStorageOptions
{
    /// <summary>
    /// Connection string to SQL Server database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database schema name (default: dbo)
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// Table name for messages storage (default: Messages)
    /// </summary>
    public string MessagesTableName { get; set; } = "Messages";

    /// <summary>
    /// Table name for outbox storage (default: OutboxMessages)
    /// </summary>
    public string OutboxTableName { get; set; } = "OutboxMessages";

    /// <summary>
    /// Table name for dead letter queue (default: DeadLetterQueue)
    /// </summary>
    public string DeadLetterTableName { get; set; } = "DeadLetterQueue";

    /// <summary>
    /// Table name for saga storage (default: Sagas)
    /// </summary>
    public string SagasTableName { get; set; } = "Sagas";

    /// <summary>
    /// Table name for inbox storage (default: InboxMessages)
    /// </summary>
    public string InboxTableName { get; set; } = "InboxMessages";

    /// <summary>
    /// Table name for queue storage (default: Queues)
    /// </summary>
    public string QueueTableName { get; set; } = "Queues";

    /// <summary>
    /// Whether to create tables automatically if they don't exist (default: true)
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>
    /// Command timeout in seconds (default: 30)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// Gets the fully qualified table name
    /// </summary>
    public string GetFullTableName(string tableName) => $"[{Schema}].[{tableName}]";
}