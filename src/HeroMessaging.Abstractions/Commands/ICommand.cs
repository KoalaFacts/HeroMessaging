using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Commands;

/// <summary>
/// Represents a command in the CQRS pattern - an imperative request to perform an action that changes system state.
/// Commands are handled by exactly one handler and may or may not return a response.
/// </summary>
/// <remarks>
/// Commands represent intent to change the system state (Create, Update, Delete operations).
/// They follow the Command pattern and are processed by <see cref="Handlers.ICommandHandler{TCommand}"/>.
///
/// Commands should:
/// - Use imperative naming (CreateOrder, UpdateCustomer, CancelSubscription)
/// - Be immutable (use records or readonly properties)
/// - Contain all data needed to perform the action
/// - Not contain business logic (logic belongs in the handler)
///
/// Example:
/// <code>
/// public record CreateOrderCommand(string CustomerId, decimal Amount) : ICommand;
/// </code>
/// </remarks>
public interface ICommand : IMessage
{
}

/// <summary>
/// Represents a command that returns a response after execution.
/// Use this when you need to return data or a result from the command execution.
/// </summary>
/// <typeparam name="TResponse">The type of response returned after command execution</typeparam>
/// <remarks>
/// Use this interface when your command needs to return:
/// - Generated identifiers (created entity IDs)
/// - Confirmation data
/// - Validation results
/// - Calculated values
///
/// Example:
/// <code>
/// public record CreateOrderCommand(string CustomerId, decimal Amount) : ICommand&lt;CreateOrderResponse&gt;;
/// public record CreateOrderResponse(string OrderId, DateTime CreatedAt);
/// </code>
/// </remarks>
public interface ICommand<TResponse> : ICommand, IMessage<TResponse>
{
}