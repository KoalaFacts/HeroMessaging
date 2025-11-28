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
    /// Table name for inbox storage (default: inbox_messages)
    /// </summary>
    public string InboxTableName { get; set; } = "inbox_messages";

    /// <summary>
    /// Table name for queue storage (default: queues)
    /// </summary>
    public string QueueTableName { get; set; } = "queues";

    /// <summary>
    /// Whether to create tables automatically if they don't exist (default: true)
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>
    /// Command timeout in seconds (default: 30)
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// Validates that a PostgreSQL identifier (schema/table name) is safe to use in SQL statements.
    /// Prevents SQL injection by rejecting unsafe characters.
    /// </summary>
    public static void ValidateSqlIdentifier(string? identifier, string paramName)
    {
        if (string.IsNullOrEmpty(identifier))
            return;

        // PostgreSQL identifiers must contain only letters, digits, and underscores
        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"Invalid SQL identifier '{identifier}'. Only letters, digits, and underscores are allowed.",
                    paramName);
            }
        }
    }

    /// <summary>
    /// Gets the fully qualified table name after validating identifiers
    /// </summary>
    public string GetFullTableName(string tableName)
    {
        ValidateSqlIdentifier(Schema, nameof(Schema));
        ValidateSqlIdentifier(tableName, nameof(tableName));
        return $"{Schema}.{tableName}";
    }
}