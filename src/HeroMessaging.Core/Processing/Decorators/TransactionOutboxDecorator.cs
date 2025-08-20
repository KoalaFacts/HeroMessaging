using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing.Decorators;

/// <summary>
/// Decorator that wraps outbox processing in database transactions
/// Ensures that outbox message processing and status updates are atomic
/// </summary>
public class TransactionOutboxProcessorDecorator : IOutboxProcessor
{
    private readonly IOutboxProcessor _inner;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<TransactionOutboxProcessorDecorator> _logger;
    private readonly IsolationLevel _defaultIsolationLevel;

    public TransactionOutboxProcessorDecorator(
        IOutboxProcessor inner,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<TransactionOutboxProcessorDecorator> logger,
        IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _logger = logger;
        _defaultIsolationLevel = defaultIsolationLevel;
    }

    public bool IsRunning => _inner.IsRunning;

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

    public ProcessorMetrics GetMetrics() => _inner.GetMetrics();

    public async Task StartAsync(CancellationToken cancellationToken = default) => 
        await _inner.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default) => 
        await _inner.StopAsync(cancellationToken);

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Decorator that wraps inbox processing in database transactions
/// Ensures that message deduplication and processing are atomic
/// </summary>
public class TransactionInboxProcessorDecorator : IInboxProcessor
{
    private readonly IInboxProcessor _inner;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly ILogger<TransactionInboxProcessorDecorator> _logger;
    private readonly IsolationLevel _defaultIsolationLevel;

    public TransactionInboxProcessorDecorator(
        IInboxProcessor inner,
        IUnitOfWorkFactory unitOfWorkFactory,
        ILogger<TransactionInboxProcessorDecorator> logger,
        IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _logger = logger;
        _defaultIsolationLevel = defaultIsolationLevel;
    }

    public bool IsRunning => _inner.IsRunning;

    public async Task ProcessIncoming(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(_defaultIsolationLevel, cancellationToken);
        
        try
        {
            _logger.LogDebug("Starting inbox transaction for message {MessageType} with ID {MessageId}", 
                message.GetType().Name, message.MessageId);

            await _inner.ProcessIncoming(message, options, cancellationToken);
            
            await unitOfWork.CommitAsync(cancellationToken);
            
            _logger.LogDebug("Inbox transaction committed successfully for message {MessageType} with ID {MessageId}", 
                message.GetType().Name, message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox transaction rollback for message {MessageType} with ID {MessageId}", 
                message.GetType().Name, message.MessageId);
            
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public ProcessorMetrics GetMetrics() => _inner.GetMetrics();

    public async Task StartAsync(CancellationToken cancellationToken = default) => 
        await _inner.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default) => 
        await _inner.StopAsync(cancellationToken);

    public void Dispose() => _inner.Dispose();
}