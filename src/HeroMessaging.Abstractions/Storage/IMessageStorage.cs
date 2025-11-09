using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IMessageStorage
{
    Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default);

    Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage;

    Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default);

    Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default);

    Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for storage transactions
/// </summary>
public interface IStorageTransaction : IDisposable
{
    /// <summary>
    /// Commits the transaction
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public class MessageStorageOptions
{
    public string? Collection { get; set; }
    public TimeSpan? Ttl { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class MessageQuery
{
    public string? Collection { get; set; }
    public DateTime? FromTimestamp { get; set; }
    public DateTime? ToTimestamp { get; set; }
    public Dictionary<string, object>? MetadataFilters { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? OrderBy { get; set; }
    public bool Ascending { get; set; } = true;
    public string? ContentContains { get; set; }
    public int MaxResults { get; set; } = 100;
}