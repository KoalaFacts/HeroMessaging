namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// Configuration options for PostgreSQL storage providers
/// </summary>
public class PostgreSqlStorageOptions
{
    /// <summary>
    /// Connection string to PostgreSQL database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database schema name (default: public)
    /// </summary>
    public string Schema { get; set; } = "public";

    /// <summary>
    /// Table name for messages storage (default: messages)
    /// </summary>
    public string MessagesTableName { get; set; } = "messages";

    /// <summary>
    /// Table name for outbox storage (default: outbox_messages)
    /// </summary>
    public string OutboxTableName { get; set; } = "outbox_messages";

    /// <summary>
    /// Table name for dead letter queue (default: dead_letter_queue)
    /// </summary>
    public string DeadLetterTableName { get; set; } = "dead_letter_queue";

    /// <summary>
    /// Table name for saga storage (default: sagas)
    /// </summary>
    public string SagasTableName { get; set; } = "sagas";

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
    public string GetFullTableName(string tableName) => $"{Schema}.{tableName}";
}