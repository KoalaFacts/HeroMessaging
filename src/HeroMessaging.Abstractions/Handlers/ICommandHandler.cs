using HeroMessaging.Abstractions.Commands;

namespace HeroMessaging.Abstractions.Handlers;

/// <summary>
/// Handles a command that does not return a response.
/// Each command type should have exactly one handler implementation.
/// </summary>
/// <typeparam name="TCommand">The type of command this handler processes</typeparam>
/// <remarks>
/// Command handlers contain the business logic for processing commands.
/// They follow the Single Responsibility Principle - one handler per command type.
///
/// Handler responsibilities:
/// - Validate command data
/// - Execute business logic
/// - Persist state changes
/// - Publish domain events
/// - Handle errors appropriately
///
/// Best practices:
/// - Keep handlers focused and single-purpose
/// - Inject dependencies via constructor
/// - Use async/await for I/O operations
/// - Throw exceptions for validation failures
/// - Return Task (not Task&lt;Unit&gt;) for void commands
///
/// Example:
/// <code>
/// public class CreateOrderCommandHandler : ICommandHandler&lt;CreateOrderCommand&gt;
/// {
///     private readonly IOrderRepository _repository;
///     private readonly IEventBus _eventBus;
///
///     public CreateOrderCommandHandler(IOrderRepository repository, IEventBus eventBus)
///     {
///         _repository = repository;
///         _eventBus = eventBus;
///     }
///
///     public async Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
///     {
///         var order = new Order(command.CustomerId, command.Amount);
///         await _repository.SaveAsync(order, cancellationToken);
///         await _eventBus.PublishAsync(new OrderCreatedEvent(order.Id, order.CustomerId), cancellationToken);
///     }
/// }
/// </code>
/// </remarks>
public interface ICommandHandler<TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Handles the specified command.
    /// </summary>
    /// <param name="command">The command to handle</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="Validation.ValidationException">Thrown when command validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled</exception>
    Task Handle(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a command that returns a response after execution.
/// Each command type should have exactly one handler implementation.
/// </summary>
/// <typeparam name="TCommand">The type of command this handler processes</typeparam>
/// <typeparam name="TResponse">The type of response returned after command execution</typeparam>
/// <remarks>
/// Use this interface when your command needs to return data to the caller, such as:
/// - Generated entity identifiers
/// - Confirmation details
/// - Calculated results
/// - Validation outcomes
///
/// Example:
/// <code>
/// public class CreateOrderCommandHandler : ICommandHandler&lt;CreateOrderCommand, CreateOrderResponse&gt;
/// {
///     private readonly IOrderRepository _repository;
///
///     public CreateOrderCommandHandler(IOrderRepository repository)
///     {
///         _repository = repository;
///     }
///
///     public async Task&lt;CreateOrderResponse&gt; Handle(CreateOrderCommand command, CancellationToken cancellationToken)
///     {
///         var order = new Order(command.CustomerId, command.Amount);
///         await _repository.SaveAsync(order, cancellationToken);
///
///         return new CreateOrderResponse(order.Id, order.CreatedAt);
///     }
/// }
/// </code>
/// </remarks>
public interface ICommandHandler<TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Handles the specified command and returns a response.
    /// </summary>
    /// <param name="command">The command to handle</param>
    /// <param name="cancellationToken">Cancellation token to abort the operation</param>
    /// <returns>A task containing the response from command execution</returns>
    /// <exception cref="Validation.ValidationException">Thrown when command validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled</exception>
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken = default);
}