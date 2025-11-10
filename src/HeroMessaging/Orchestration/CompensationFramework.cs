using Microsoft.Extensions.Logging;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Represents a compensating action that can undo a previous operation
/// </summary>
public interface ICompensatingAction
{
    /// <summary>
    /// Name of the compensating action for logging and tracking
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Execute the compensation logic
    /// </summary>
    Task CompensateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for managing compensation actions during saga execution
/// Tracks all compensating actions and can execute them in reverse order
/// </summary>
public class CompensationContext
{
    private readonly Stack<ICompensatingAction> _actions = new();
    private readonly ILogger<CompensationContext>? _logger;

    public CompensationContext(ILogger<CompensationContext>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Add a compensating action to the stack
    /// Actions are executed in LIFO order (last registered, first compensated)
    /// </summary>
    public void AddCompensation(ICompensatingAction action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        _actions.Push(action);
        _logger?.LogDebug("Added compensation action: {ActionName}", action.ActionName);
    }

    /// <summary>
    /// Execute all registered compensating actions in reverse order
    /// </summary>
    /// <param name="stopOnFirstError">If true, stops compensation on first error. If false, attempts all compensations.</param>
    public async Task CompensateAsync(bool stopOnFirstError = false, CancellationToken cancellationToken = default)
    {
        var exceptions = new List<Exception>();
        var compensatedCount = 0;

        _logger?.LogInformation("Starting compensation of {Count} actions", _actions.Count);

        while (_actions.Count > 0)
        {
            var action = _actions.Pop();

            try
            {
                _logger?.LogInformation("Compensating action: {ActionName}", action.ActionName);
                await action.CompensateAsync(cancellationToken);
                compensatedCount++;
                _logger?.LogDebug("Successfully compensated action: {ActionName}", action.ActionName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to compensate action: {ActionName}", action.ActionName);
                exceptions.Add(new CompensationException(action.ActionName, ex));

                if (stopOnFirstError)
                {
                    throw new AggregateException(
                        $"Compensation failed for action '{action.ActionName}'. Stopped after {compensatedCount} successful compensations.",
                        exceptions);
                }
            }
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException(
                $"Compensation completed with {exceptions.Count} errors out of {compensatedCount + exceptions.Count} actions",
                exceptions);
        }

        _logger?.LogInformation("Successfully compensated {Count} actions", compensatedCount);
    }

    /// <summary>
    /// Check if there are any actions to compensate
    /// </summary>
    public bool HasActions => _actions.Count > 0;

    /// <summary>
    /// Get count of registered compensation actions
    /// </summary>
    public int ActionCount => _actions.Count;

    /// <summary>
    /// Clear all registered actions without executing them
    /// </summary>
    public void Clear()
    {
        _actions.Clear();
        _logger?.LogDebug("Cleared all compensation actions");
    }
}

/// <summary>
/// Exception thrown when a compensation action fails
/// </summary>
public class CompensationException : Exception
{
    public string ActionName { get; }

    public CompensationException(string actionName, Exception innerException)
        : base($"Failed to compensate action '{actionName}': {innerException.Message}", innerException)
    {
        ActionName = actionName;
    }
}

/// <summary>
/// Lambda-based compensating action for simple scenarios
/// </summary>
public class DelegateCompensatingAction : ICompensatingAction
{
    private readonly Func<CancellationToken, Task> _compensateFunc;

    public string ActionName { get; }

    public DelegateCompensatingAction(string actionName, Func<CancellationToken, Task> compensateFunc)
    {
        ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
        _compensateFunc = compensateFunc ?? throw new ArgumentNullException(nameof(compensateFunc));
    }

    public DelegateCompensatingAction(string actionName, Func<Task> compensateFunc)
        : this(actionName, _ => compensateFunc())
    {
    }

    public DelegateCompensatingAction(string actionName, Action compensateAction)
        : this(actionName, _ =>
        {
            compensateAction();
            return Task.CompletedTask;
        })
    {
    }

    public Task CompensateAsync(CancellationToken cancellationToken = default)
    {
        return _compensateFunc(cancellationToken);
    }
}

/// <summary>
/// Extension methods for easier compensation context usage
/// </summary>
public static class CompensationExtensions
{
    /// <summary>
    /// Add a compensation action using a delegate
    /// </summary>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Func<CancellationToken, Task> compensateFunc)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateFunc));
    }

    /// <summary>
    /// Add a compensation action using a simple action
    /// </summary>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Action compensateAction)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateAction));
    }

    /// <summary>
    /// Add a compensation action using an async task
    /// </summary>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Func<Task> compensateFunc)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateFunc));
    }
}
