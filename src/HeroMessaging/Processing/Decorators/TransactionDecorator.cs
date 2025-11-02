using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;
using System.Data;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that wraps command and query processing in database transactions
/// </summary>
public class TransactionCommandProcessorDecorator(
    ICommandProcessor inner,
    ITransactionExecutor transactionExecutor,
    IsolationLevel defaultIsolationLevel = IsolationLevel.ReadCommitted) : ICommandProcessor
{
    private readonly ICommandProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));
    private readonly IsolationLevel _defaultIsolationLevel = defaultIsolationLevel;

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.Send(command, ct),
            $"command {command.GetType().Name} with ID {command.MessageId}",
            _defaultIsolationLevel,
            cancellationToken);
    }

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.Send<TResponse>(command, ct),
            $"command {command.GetType().Name} with ID {command.MessageId}",
            _defaultIsolationLevel,
            cancellationToken);
    }
}

/// <summary>
/// Decorator that wraps query processing in read-only database transactions
/// </summary>
public class TransactionQueryProcessorDecorator(
    IQueryProcessor inner,
    ITransactionExecutor transactionExecutor) : IQueryProcessor
{
    private readonly IQueryProcessor _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ITransactionExecutor _transactionExecutor = transactionExecutor ?? throw new ArgumentNullException(nameof(transactionExecutor));

    public async Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.Send<TResponse>(query, ct),
            $"query {query.GetType().Name} with ID {query.MessageId}",
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }
}