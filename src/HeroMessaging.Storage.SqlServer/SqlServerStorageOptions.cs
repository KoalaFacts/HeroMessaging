using System.Text.RegularExpressions;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// Configuration options for SQL Server storage providers
/// </summary>
public partial class SqlServerStorageOptions
{
    // Regex for valid SQL identifiers: alphanumeric and underscores, starting with letter or underscore
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex SqlIdentifierRegex();
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
    /// Gets the fully qualified table name with SQL injection validation
    /// </summary>
    /// <param name="tableName">The table name to qualify</param>
    /// <returns>Fully qualified table name in format [Schema].[TableName]</returns>
    /// <exception cref="ArgumentException">Thrown when schema or table name contains invalid characters</exception>
    public string GetFullTableName(string tableName)
    {
        ValidateSqlIdentifier(Schema, nameof(Schema));
        ValidateSqlIdentifier(tableName, nameof(tableName));
        return $"[{Schema}].[{tableName}]";
    }

    /// <summary>
    /// Validates that an identifier contains only safe SQL characters.
    /// SECURITY: Prevents SQL injection by rejecting identifiers with special characters.
    /// </summary>
    /// <param name="identifier">The identifier to validate</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <exception cref="ArgumentException">Thrown when identifier contains invalid characters</exception>
    public static void ValidateSqlIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be empty", parameterName);

        if (!SqlIdentifierRegex().IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Invalid SQL identifier '{identifier}'. Only alphanumeric characters and underscores are allowed, and it must start with a letter or underscore.",
                parameterName);
        }
    }
}