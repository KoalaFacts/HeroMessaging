using HeroMessaging.Abstractions.Sagas;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Orchestration;

/// <summary>
/// Represents a compensating action that can undo a previous operation.
/// Used in saga patterns to implement rollback logic when a saga step fails.
/// </summary>
/// <remarks>
/// Compensating actions implement the undo logic for operations that have been completed.
/// They are registered with a <see cref="CompensationContext"/> and executed in reverse order
/// when compensation is needed.
///
/// Example:
/// <code>
/// public class ReservationCompensation : ICompensatingAction
/// {
///     private readonly IReservationService _service;
///     private readonly Guid _reservationId;
///
///     public string ActionName => "CancelReservation";
///
///     public async Task CompensateAsync(CancellationToken cancellationToken)
///     {
///         await _service.CancelReservationAsync(_reservationId, cancellationToken);
///     }
/// }
/// </code>
/// </remarks>
public interface ICompensatingAction
{
    /// <summary>
    /// Gets the name of the compensating action for logging and tracking purposes.
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Executes the compensation logic to undo a previous operation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous compensation operation.</returns>
    Task CompensateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for managing compensation actions during saga execution.
/// Tracks all compensating actions and executes them in reverse order when compensation is needed.
/// </summary>
/// <remarks>
/// The compensation context implements a stack-based compensation pattern where actions are
/// executed in LIFO (Last In, First Out) order. This ensures that operations are undone
/// in the reverse order they were performed.
///
/// Example usage in a saga:
/// <code>
/// var context = new CompensationContext(logger);
///
/// try
/// {
///     // Step 1: Reserve inventory
///     var reservationId = await inventoryService.ReserveAsync(orderId);
///     context.AddCompensation("ReleaseInventory", async ct =>
///         await inventoryService.ReleaseAsync(reservationId, ct));
///
///     // Step 2: Charge payment
///     var chargeId = await paymentService.ChargeAsync(amount);
///     context.AddCompensation("RefundPayment", async ct =>
///         await paymentService.RefundAsync(chargeId, ct));
///
///     // Step 3: Send confirmation
///     await notificationService.SendConfirmationAsync(orderId);
/// }
/// catch (Exception)
/// {
///     // Compensate in reverse order: refund payment, then release inventory
///     await context.CompensateAsync(cancellationToken: cancellationToken);
///     throw;
/// }
/// </code>
/// </remarks>
public class CompensationContext
{
    private readonly Stack<ICompensatingAction> _actions = new();
    private readonly ILogger<CompensationContext>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompensationContext"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for tracking compensation execution.</param>
    public CompensationContext(ILogger<CompensationContext>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds a compensating action to the stack.
    /// Actions are executed in LIFO order (last registered, first compensated).
    /// </summary>
    /// <param name="action">The compensating action to add to the stack.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
    public void AddCompensation(ICompensatingAction action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        _actions.Push(action);
        _logger?.LogDebug("Added compensation action: {ActionName}", action.ActionName);
    }

    /// <summary>
    /// Executes all registered compensating actions in reverse order (LIFO).
    /// </summary>
    /// <param name="stopOnFirstError">If true, stops compensation on first error. If false, attempts all compensations and aggregates errors.</param>
    /// <param name="cancellationToken">Cancellation token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous compensation operation.</returns>
    /// <exception cref="AggregateException">Thrown when one or more compensation actions fail.</exception>
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
    /// Gets a value indicating whether there are any actions to compensate.
    /// </summary>
    public bool HasActions => _actions.Count > 0;

    /// <summary>
    /// Gets the count of registered compensation actions in the stack.
    /// </summary>
    public int ActionCount => _actions.Count;

    /// <summary>
    /// Clears all registered actions without executing them.
    /// Use this to reset the compensation context after successful completion.
    /// </summary>
    public void Clear()
    {
        _actions.Clear();
        _logger?.LogDebug("Cleared all compensation actions");
    }
}

/// <summary>
/// Exception thrown when a compensation action fails during saga rollback.
/// </summary>
/// <remarks>
/// This exception wraps the underlying failure that occurred during compensation execution.
/// It includes the name of the failed action to aid in diagnostics and troubleshooting.
/// </remarks>
public class CompensationException : Exception
{
    /// <summary>
    /// Gets the name of the compensating action that failed.
    /// </summary>
    public string ActionName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompensationException"/> class with the action name and underlying exception.
    /// </summary>
    /// <param name="actionName">The name of the compensating action that failed.</param>
    /// <param name="innerException">The exception that occurred during compensation execution.</param>
    public CompensationException(string actionName, Exception innerException)
        : base($"Failed to compensate action '{actionName}': {innerException.Message}", innerException)
    {
        ActionName = actionName;
    }
}

/// <summary>
/// Lambda-based compensating action for simple scenarios where a full class implementation is not needed.
/// </summary>
/// <remarks>
/// This class provides a convenient way to create compensating actions using delegates,
/// reducing boilerplate code for simple compensation logic.
///
/// Example:
/// <code>
/// // Using async delegate with cancellation token
/// var action1 = new DelegateCompensatingAction(
///     "RefundPayment",
///     async ct => await paymentService.RefundAsync(chargeId, ct));
///
/// // Using async delegate without cancellation token
/// var action2 = new DelegateCompensatingAction(
///     "ReleaseInventory",
///     async () => await inventoryService.ReleaseAsync(reservationId));
///
/// // Using synchronous action
/// var action3 = new DelegateCompensatingAction(
///     "DecrementCounter",
///     () => counter--);
/// </code>
/// </remarks>
public class DelegateCompensatingAction : ICompensatingAction
{
    private readonly Func<CancellationToken, Task> _compensateFunc;

    /// <summary>
    /// Gets the name of the compensating action for logging and tracking purposes.
    /// </summary>
    public string ActionName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateCompensatingAction"/> class with an async delegate that accepts a cancellation token.
    /// </summary>
    /// <param name="actionName">The name of the compensating action.</param>
    /// <param name="compensateFunc">The async function to execute for compensation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actionName"/> or <paramref name="compensateFunc"/> is null.</exception>
    public DelegateCompensatingAction(string actionName, Func<CancellationToken, Task> compensateFunc)
    {
        ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
        _compensateFunc = compensateFunc ?? throw new ArgumentNullException(nameof(compensateFunc));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateCompensatingAction"/> class with an async delegate that does not use a cancellation token.
    /// </summary>
    /// <param name="actionName">The name of the compensating action.</param>
    /// <param name="compensateFunc">The async function to execute for compensation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actionName"/> or <paramref name="compensateFunc"/> is null.</exception>
    public DelegateCompensatingAction(string actionName, Func<Task> compensateFunc)
        : this(actionName, _ => compensateFunc())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateCompensatingAction"/> class with a synchronous action.
    /// </summary>
    /// <param name="actionName">The name of the compensating action.</param>
    /// <param name="compensateAction">The synchronous action to execute for compensation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actionName"/> or <paramref name="compensateAction"/> is null.</exception>
    public DelegateCompensatingAction(string actionName, Action compensateAction)
        : this(actionName, _ =>
        {
            compensateAction();
            return Task.CompletedTask;
        })
    {
    }

    /// <summary>
    /// Executes the compensation logic using the registered delegate.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous compensation operation.</returns>
    public Task CompensateAsync(CancellationToken cancellationToken = default)
    {
        return _compensateFunc(cancellationToken);
    }
}

/// <summary>
/// Extension methods for easier compensation context usage with delegate-based compensation actions.
/// </summary>
public static class CompensationExtensions
{
    /// <summary>
    /// Adds a compensation action using an async delegate that accepts a cancellation token.
    /// </summary>
    /// <param name="context">The compensation context to add the action to.</param>
    /// <param name="actionName">The name of the compensating action for logging and tracking.</param>
    /// <param name="compensateFunc">The async function to execute for compensation.</param>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Func<CancellationToken, Task> compensateFunc)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateFunc));
    }

    /// <summary>
    /// Adds a compensation action using a synchronous action.
    /// </summary>
    /// <param name="context">The compensation context to add the action to.</param>
    /// <param name="actionName">The name of the compensating action for logging and tracking.</param>
    /// <param name="compensateAction">The synchronous action to execute for compensation.</param>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Action compensateAction)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateAction));
    }

    /// <summary>
    /// Adds a compensation action using an async delegate that does not use a cancellation token.
    /// </summary>
    /// <param name="context">The compensation context to add the action to.</param>
    /// <param name="actionName">The name of the compensating action for logging and tracking.</param>
    /// <param name="compensateFunc">The async function to execute for compensation.</param>
    public static void AddCompensation(
        this CompensationContext context,
        string actionName,
        Func<Task> compensateFunc)
    {
        context.AddCompensation(new DelegateCompensatingAction(actionName, compensateFunc));
    }
}
