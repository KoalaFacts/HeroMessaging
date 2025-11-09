using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
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

    public async Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.PublishToOutbox(message, options, ct),
            $"outbox message {message.GetType().Name} with ID {message.MessageId}",
            _defaultIsolationLevel,
            cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _inner.StartAsync(cancellationToken);

    public Task StopAsync() =>
        _inner.StopAsync();
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

    public async Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.ProcessIncoming(message, options, ct),
            $"inbox message {message.GetType().Name} with ID {message.MessageId}",
            _defaultIsolationLevel,
            cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _inner.StartAsync(cancellationToken);

    public Task StopAsync() =>
        _inner.StopAsync();

    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default) =>
        _inner.GetUnprocessedCount(cancellationToken);
}