using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using System.Data;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that wraps outbox processing in database transactions
/// Ensures that outbox message processing and status updates are atomic
/// </summary>
public class TransactionOutboxProcessorDecorator(
    IOutboxProcessor inner,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<TransactionOutboxProcessorDecorator> logger,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : IOutboxProcessor
{
    private readonly IOutboxProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    private readonly ILogger<TransactionOutboxProcessorDecorator> _logger = logger;
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;

    public async Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(_defaultIsolationLevel, cancellationToken);

        try
        {
            _logger.LogDebug("Starting outbox transaction for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);

            await _inner.PublishToOutbox(message, options, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            _logger.LogDebug("Outbox transaction committed successfully for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outbox transaction rollback for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);

            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task Start(CancellationToken cancellationToken = default) =>
        _inner.Start(cancellationToken);

    public Task Stop() =>
        _inner.Stop();
}

/// <summary>
/// Decorator that wraps inbox processing in database transactions
/// Ensures that message deduplication and processing are atomic
/// </summary>
public class TransactionInboxProcessorDecorator(
    IInboxProcessor inner,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<TransactionInboxProcessorDecorator> logger,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : IInboxProcessor
{
    private readonly IInboxProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    private readonly ILogger<TransactionInboxProcessorDecorator> _logger = logger;
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;

    public async Task<bool> ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(_defaultIsolationLevel, cancellationToken);

        try
        {
            _logger.LogDebug("Starting inbox transaction for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);

            var result = await _inner.ProcessIncoming(message, options, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            _logger.LogDebug("Inbox transaction committed successfully for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox transaction rollback for message {MessageType} with ID {MessageId}",
                message.GetType().Name, message.MessageId);

            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task Start(CancellationToken cancellationToken = default) =>
        _inner.Start(cancellationToken);

    public Task Stop() =>
        _inner.Stop();

    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default) =>
        _inner.GetUnprocessedCount(cancellationToken);
}