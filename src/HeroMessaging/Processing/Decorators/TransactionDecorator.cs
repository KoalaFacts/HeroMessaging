using System.Data;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Abstractions.Storage;

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

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.SendAsync(command, ct).ConfigureAwait(false),
            $"command {command.GetType().Name} with ID {command.MessageId}",
            _defaultIsolationLevel,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.SendAsync<TResponse>(command, ct).ConfigureAwait(false),
            $"command {command.GetType().Name} with ID {command.MessageId}",
            _defaultIsolationLevel,
            cancellationToken).ConfigureAwait(false);
    }

    public bool IsRunning => (_inner as IProcessor)?.IsRunning ?? true;

    public IProcessorMetrics GetMetrics() => _inner.GetMetrics();
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

    public async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteInTransactionAsync(
            async ct => await _inner.SendAsync<TResponse>(query, ct).ConfigureAwait(false),
            $"query {query.GetType().Name} with ID {query.MessageId}",
            IsolationLevel.ReadCommitted,
            cancellationToken).ConfigureAwait(false);
    }

    public bool IsRunning => (_inner as IProcessor)?.IsRunning ?? true;

    public IQueryProcessorMetrics GetMetrics() => _inner.GetMetrics();
}
