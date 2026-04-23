using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that wraps outbox processing in database transactions
/// Ensures that outbox message processing and status updates are atomic
/// </summary>
public class TransactionOutboxProcessorDecorator(
    IOutboxProcessor inner,
    ITransactionExecutor transactionExecutor,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : IOutboxProcessor
{
    private readonly IOutboxProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;
    /// <summary>
    /// Executes publish to outbox async.
    /// </summary>

    public async Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.PublishToOutboxAsync(message, options, ct).ConfigureAwait(false),
            $"outbox message {message.GetType().Name} with ID {message.MessageId}",
            _defaultIsolationLevel,
            cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Executes start async.
    /// </summary>

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _inner.StartAsync(cancellationToken);
    /// <summary>
    /// Executes stop async.
    /// </summary>

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _inner.StopAsync(cancellationToken);
    /// <summary>
    /// Gets is running.
    /// </summary>

    public bool IsRunning => _inner.IsRunning;
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IOutboxProcessorMetrics GetMetrics() => _inner.GetMetrics();
}

/// <summary>
/// Decorator that wraps inbox processing in database transactions
/// Ensures that message deduplication and processing are atomic
/// </summary>
public class TransactionInboxProcessorDecorator(
    IInboxProcessor inner,
    ITransactionExecutor transactionExecutor,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : IInboxProcessor
{
    private readonly IInboxProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;
    /// <summary>
    /// Executes process incoming async.
    /// </summary>

    public async Task<bool> ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.ProcessIncomingAsync(message, options, ct).ConfigureAwait(false),
            $"inbox message {message.GetType().Name} with ID {message.MessageId}",
            _defaultIsolationLevel,
            cancellationToken).ConfigureAwait(false);
    }
    /// <summary>
    /// Executes start async.
    /// </summary>

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _inner.StartAsync(cancellationToken);
    /// <summary>
    /// Executes stop async.
    /// </summary>

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _inner.StopAsync(cancellationToken);
    /// <summary>
    /// Executes get unprocessed count async.
    /// </summary>

    public Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default) =>
        _inner.GetUnprocessedCountAsync(cancellationToken);
    /// <summary>
    /// Gets is running.
    /// </summary>

    public bool IsRunning => _inner.IsRunning;
    /// <summary>
    /// Executes get metrics.
    /// </summary>

    public IInboxProcessorMetrics GetMetrics() => _inner.GetMetrics();
}
