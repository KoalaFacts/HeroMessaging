using System.Data;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that wraps command and query processing in database transactions
/// </summary>
public class TransactionCommandProcessorDecorator(
    ICommandProcessor inner,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<TransactionCommandProcessorDecorator> logger,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : ICommandProcessor
{
    private readonly ICommandProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    private readonly ILogger<TransactionCommandProcessorDecorator> _logger = logger;
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(_defaultIsolationLevel, cancellationToken);
        
        try
        {
            _logger.LogDebug("Starting transaction for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);

            await _inner.Send(command, cancellationToken);
            
            await unitOfWork.CommitAsync(cancellationToken);
            
            _logger.LogDebug("Transaction committed successfully for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction rollback for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);
            
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(_defaultIsolationLevel, cancellationToken);
        
        try
        {
            _logger.LogDebug("Starting transaction for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);

            var result = await _inner.Send<TResponse>(command, cancellationToken);
            
            await unitOfWork.CommitAsync(cancellationToken);
            
            _logger.LogDebug("Transaction committed successfully for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction rollback for command {CommandType} with ID {CommandId}", 
                command.GetType().Name, command.MessageId);
            
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

/// <summary>
/// Decorator that wraps query processing in read-only database transactions
/// </summary>
public class TransactionQueryProcessorDecorator(
    IQueryProcessor inner,
    IUnitOfWorkFactory unitOfWorkFactory,
    ILogger<TransactionQueryProcessorDecorator> logger) : IQueryProcessor
{
    private readonly IQueryProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitOfWorkFactory _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
    private readonly ILogger<TransactionQueryProcessorDecorator> _logger = logger;

    public async Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await _unitOfWorkFactory.CreateAsync(IsolationLevel.ReadCommitted, cancellationToken);
        
        try
        {
            _logger.LogDebug("Starting read transaction for query {QueryType} with ID {QueryId}", 
                query.GetType().Name, query.MessageId);

            var result = await _inner.Send<TResponse>(query, cancellationToken);
            
            // Read-only operations don't need explicit commit, but we can commit for consistency
            await unitOfWork.CommitAsync(cancellationToken);
            
            _logger.LogDebug("Read transaction completed for query {QueryType} with ID {QueryId}", 
                query.GetType().Name, query.MessageId);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read transaction rollback for query {QueryType} with ID {QueryId}", 
                query.GetType().Name, query.MessageId);
            
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}