using System.Data;
using System.Text.Json;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of dead letter queue using pure ADO.NET
/// </summary>
public class SqlServerDeadLetterQueue : IDeadLetterQueue
{
    private readonly SqlServerStorageOptions _options;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _tableName;

    public SqlServerDeadLetterQueue(SqlServerStorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = options.ConnectionString;
        _tableName = _options.GetFullTableName(_options.DeadLetterTableName);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        
        InitializeDatabase().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabase()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var createTableSql = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DeadLetterQueue')
            BEGIN
                CREATE TABLE DeadLetterQueue (
                    Id NVARCHAR(100) PRIMARY KEY,
                    MessagePayload NVARCHAR(MAX) NOT NULL,
                    MessageType NVARCHAR(500) NOT NULL,
                    Reason NVARCHAR(MAX) NOT NULL,
                    Component NVARCHAR(200) NOT NULL,
                    RetryCount INT NOT NULL,
                    FailureTime DATETIME2 NOT NULL,
                    Status INT NOT NULL DEFAULT 0, -- 0: Active, 1: Retried, 2: Discarded, 3: Expired
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    RetriedAt DATETIME2 NULL,
                    DiscardedAt DATETIME2 NULL,
                    ExceptionMessage NVARCHAR(MAX) NULL,
                    Metadata NVARCHAR(MAX) NULL,
                    INDEX IX_DeadLetterQueue_Status (Status),
                    INDEX IX_DeadLetterQueue_MessageType (MessageType),
                    INDEX IX_DeadLetterQueue_FailureTime (FailureTime DESC)
                )
            END
            """;
        
        using var command = new SqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) 
        where T : IMessage
    {
        var deadLetterId = Guid.NewGuid().ToString();
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var sql = """
            INSERT INTO DeadLetterQueue (
                Id, MessagePayload, MessageType, Reason, Component, 
                RetryCount, FailureTime, Status, CreatedAt, 
                ExceptionMessage, Metadata
            )
            VALUES (
                @Id, @MessagePayload, @MessageType, @Reason, @Component,
                @RetryCount, @FailureTime, @Status, @CreatedAt,
                @ExceptionMessage, @Metadata
            )
            """;
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = deadLetterId;
        command.Parameters.Add("@MessagePayload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(message, _jsonOptions);
        command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = message.GetType().FullName ?? "Unknown";
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, -1).Value = context.Reason;
        command.Parameters.Add("@Component", SqlDbType.NVarChar, 200).Value = context.Component;
        command.Parameters.Add("@RetryCount", SqlDbType.Int).Value = context.RetryCount;
        command.Parameters.Add("@FailureTime", SqlDbType.DateTime2).Value = context.FailureTime;
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)DeadLetterStatus.Active;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTime2).Value = DateTime.UtcNow;
        command.Parameters.Add("@ExceptionMessage", SqlDbType.NVarChar, -1).Value = (object?)context.Exception?.Message ?? DBNull.Value;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = 
            context.Metadata.Any() ? JsonSerializer.Serialize(context.Metadata, _jsonOptions) : (object)DBNull.Value;
        
        await command.ExecuteNonQueryAsync(cancellationToken);
        return deadLetterId;
    }

    public async Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default) 
        where T : IMessage
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var sql = """
            SELECT TOP(@Limit)
                Id, MessagePayload, Reason, Component, RetryCount, FailureTime,
                Status, CreatedAt, RetriedAt, DiscardedAt, ExceptionMessage, Metadata
            FROM DeadLetterQueue
            WHERE MessageType = @MessageType AND Status = @ActiveStatus
            ORDER BY FailureTime DESC
            """;
        
        var entries = new List<DeadLetterEntry<T>>();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@MessageType", SqlDbType.NVarChar, 500).Value = typeof(T).FullName ?? "Unknown";
        command.Parameters.Add("@ActiveStatus", SqlDbType.Int).Value = (int)DeadLetterStatus.Active;
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var messagePayload = reader.GetString(1);
            var message = JsonSerializer.Deserialize<T>(messagePayload, _jsonOptions);
            
            if (message != null)
            {
                var metadataJson = reader.IsDBNull(11) ? null : reader.GetString(11);
                var metadata = !string.IsNullOrEmpty(metadataJson) 
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _jsonOptions) ?? new()
                    : new Dictionary<string, object>();
                
                entries.Add(new DeadLetterEntry<T>
                {
                    Id = reader.GetString(0),
                    Message = message,
                    Context = new DeadLetterContext
                    {
                        Reason = reader.GetString(2),
                        Component = reader.GetString(3),
                        RetryCount = reader.GetInt32(4),
                        FailureTime = reader.GetDateTime(5),
                        Exception = reader.IsDBNull(10) ? null : new Exception(reader.GetString(10)),
                        Metadata = metadata
                    },
                    Status = (DeadLetterStatus)reader.GetInt32(6),
                    CreatedAt = reader.GetDateTime(7),
                    RetriedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    DiscardedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
                });
            }
        }
        
        return entries;
    }

    public async Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default) 
        where T : IMessage
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var sql = """
            UPDATE DeadLetterQueue
            SET Status = @Status, RetriedAt = @RetriedAt
            WHERE Id = @Id AND Status = @ActiveStatus
            """;
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)DeadLetterStatus.Retried;
        command.Parameters.Add("@RetriedAt", SqlDbType.DateTime2).Value = DateTime.UtcNow;
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = deadLetterId;
        command.Parameters.Add("@ActiveStatus", SqlDbType.Int).Value = (int)DeadLetterStatus.Active;
        
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var sql = """
            UPDATE DeadLetterQueue
            SET Status = @Status, DiscardedAt = @DiscardedAt
            WHERE Id = @Id AND Status = @ActiveStatus
            """;
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)DeadLetterStatus.Discarded;
        command.Parameters.Add("@DiscardedAt", SqlDbType.DateTime2).Value = DateTime.UtcNow;
        command.Parameters.Add("@Id", SqlDbType.NVarChar, 100).Value = deadLetterId;
        command.Parameters.Add("@ActiveStatus", SqlDbType.Int).Value = (int)DeadLetterStatus.Active;
        
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        return result > 0;
    }

    public async Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var sql = "SELECT COUNT(*) FROM DeadLetterQueue WHERE Status = @Status";
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Status", SqlDbType.Int).Value = (int)DeadLetterStatus.Active;
        
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result ?? 0);
    }

    public async Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var statistics = new DeadLetterStatistics();
        
        // Get counts by status
        var statusSql = """
            SELECT 
                SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) as ActiveCount,
                SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) as RetriedCount,
                SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) as DiscardedCount,
                COUNT(*) as TotalCount
            FROM DeadLetterQueue
            """;
        
        using (var statusCommand = new SqlCommand(statusSql, connection))
        using (var reader = await statusCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                statistics.ActiveCount = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                statistics.RetriedCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                statistics.DiscardedCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                statistics.TotalCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            }
        }
        
        // Get counts by component
        var componentSql = """
            SELECT Component, COUNT(*) as Count
            FROM DeadLetterQueue
            WHERE Status = 0
            GROUP BY Component
            """;
        
        using (var componentCommand = new SqlCommand(componentSql, connection))
        using (var reader = await componentCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statistics.CountByComponent[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        
        // Get counts by reason (top 10)
        var reasonSql = """
            SELECT TOP 10 Reason, COUNT(*) as Count
            FROM DeadLetterQueue
            WHERE Status = 0
            GROUP BY Reason
            ORDER BY COUNT(*) DESC
            """;
        
        using (var reasonCommand = new SqlCommand(reasonSql, connection))
        using (var reader = await reasonCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statistics.CountByReason[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        
        // Get oldest and newest entries
        var dateSql = """
            SELECT 
                MIN(CreatedAt) as OldestEntry,
                MAX(CreatedAt) as NewestEntry
            FROM DeadLetterQueue
            WHERE Status = 0
            """;
        
        using (var dateCommand = new SqlCommand(dateSql, connection))
        using (var reader = await dateCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                statistics.OldestEntry = reader.IsDBNull(0) ? null : reader.GetDateTime(0);
                statistics.NewestEntry = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
            }
        }
        
        return statistics;
    }
}