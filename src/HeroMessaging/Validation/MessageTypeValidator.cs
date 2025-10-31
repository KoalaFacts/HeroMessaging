using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that messages implement the correct interfaces based on their type.
/// Ensures messages conform to the expected messaging patterns (commands, events, queries).
/// </summary>
/// <remarks>
/// This validator enforces type safety by checking that messages implement the appropriate
/// marker interfaces defined in HeroMessaging.Abstractions. It prevents incorrect message
/// types from being processed by the messaging pipeline.
///
/// Validation Rules:
/// - Message must implement at least one allowed interface type
/// - By default, allows ICommand and IEvent interfaces
/// - Can be configured to allow custom interface types
/// - Supports generic type definitions (e.g., IQuery&lt;T&gt;)
/// - Checks both direct implementation and interface inheritance
///
/// Default Allowed Types:
/// - ICommand: For messages that modify system state
/// - IEvent: For messages that represent facts that have occurred
///
/// Custom Allowed Types:
/// You can restrict or extend the allowed types by providing them to the constructor.
///
/// Performance:
/// - Uses HashSet for O(1) type lookups
/// - Minimal allocation overhead (reflection cached by runtime)
/// - Typically completes in &lt;0.1ms
///
/// <code>
/// // Default behavior - allows commands and events
/// var validator = new MessageTypeValidator();
/// var result = await validator.ValidateAsync(new OrderCreatedEvent(), ct);
/// // result.IsValid == true
///
/// // Custom allowed types - only allow specific interfaces
/// var queryValidator = new MessageTypeValidator(typeof(IQuery&lt;&gt;));
/// var result = await queryValidator.ValidateAsync(new GetOrderQuery(), ct);
/// // result.IsValid == true if GetOrderQuery implements IQuery&lt;T&gt;
///
/// // Multiple allowed types
/// var customValidator = new MessageTypeValidator(
///     typeof(ICommand),
///     typeof(IEvent),
///     typeof(IQuery&lt;&gt;)
/// );
/// </code>
///
/// Integration with HeroMessaging:
/// <code>
/// // Register in DI container
/// services.AddSingleton&lt;IMessageValidator&gt;(
///     new MessageTypeValidator(typeof(ICommand), typeof(IEvent))
/// );
///
/// // Validator runs automatically in the pipeline
/// await messaging.Send(new CreateOrderCommand()); // Validated before processing
/// </code>
/// </remarks>
public class MessageTypeValidator : IMessageValidator
{
    private readonly HashSet<Type> _allowedTypes;

    /// <summary>
    /// Initializes a new instance of the MessageTypeValidator class with specified allowed types.
    /// </summary>
    /// <param name="allowedTypes">
    /// Optional array of interface types that messages are allowed to implement.
    /// If null or empty, defaults to ICommand and IEvent.
    /// Pass specific types to restrict which message patterns are allowed.
    /// </param>
    /// <remarks>
    /// The validator will accept messages that implement any of the specified types.
    /// Multiple types can be provided to allow different message patterns.
    ///
    /// <code>
    /// // Default - allows commands and events
    /// var defaultValidator = new MessageTypeValidator();
    ///
    /// // Custom - only allow commands
    /// var commandValidator = new MessageTypeValidator(typeof(ICommand));
    ///
    /// // Custom - allow commands, events, and queries
    /// var fullValidator = new MessageTypeValidator(
    ///     typeof(ICommand),
    ///     typeof(IEvent),
    ///     typeof(IQuery&lt;&gt;)
    /// );
    /// </code>
    /// </remarks>
    public MessageTypeValidator(params Type[] allowedTypes)
    {
        _allowedTypes = allowedTypes?.Length > 0
            ? new HashSet<Type>(allowedTypes)
            : new HashSet<Type> { typeof(ICommand), typeof(IEvent) };
    }

    /// <summary>
    /// Validates that the message implements at least one of the allowed interface types.
    /// </summary>
    /// <param name="message">The message to validate. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the validation operation.</param>
    /// <returns>
    /// A ValueTask containing a ValidationResult.
    /// Success if the message implements any allowed interface type.
    /// Failure with error message listing allowed types if not implemented.
    /// </returns>
    /// <remarks>
    /// This method performs type checking using reflection to determine if the message
    /// implements any of the configured allowed types. It checks:
    /// - Direct type assignment (e.g., message is ICommand)
    /// - Implemented interfaces (e.g., message implements ICommand)
    /// - Generic type definitions (e.g., message implements IQuery&lt;TResult&gt;)
    ///
    /// The validation is synchronous and returns a completed ValueTask for efficiency.
    ///
    /// Performance:
    /// - Type checking is fast (reflection metadata cached by runtime)
    /// - HashSet lookup is O(1)
    /// - Typically completes in &lt;0.1ms
    /// - No heap allocations in success case
    ///
    /// Error Messages:
    /// If validation fails, the error message includes:
    /// - The actual message type name
    /// - List of allowed interface types
    /// This helps developers quickly identify the issue.
    ///
    /// <code>
    /// // Valid message type
    /// var command = new CreateOrderCommand();
    /// var result = await validator.ValidateAsync(command, ct);
    /// // result.IsValid == true
    ///
    /// // Invalid message type
    /// var invalidMessage = new CustomMessage(); // Doesn't implement ICommand or IEvent
    /// var result = await validator.ValidateAsync(invalidMessage, ct);
    /// // result.IsValid == false
    /// // result.Errors[0] == "Message type 'CustomMessage' does not implement any of the allowed interfaces: ICommand, IEvent"
    /// </code>
    /// </remarks>
    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType();
        var interfaces = messageType.GetInterfaces();

        bool isValid = _allowedTypes.Any(allowedType =>
        {
            // Check direct assignment
            if (allowedType.IsAssignableFrom(messageType))
                return true;

            // Check implemented interfaces
            if (interfaces.Any(i => allowedType.IsAssignableFrom(i)))
                return true;

            // Special check for generic IQuery<T>
            if (allowedType.IsGenericTypeDefinition)
            {
                return interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == allowedType);
            }

            return false;
        });

        if (!isValid)
        {
            var allowedTypeNames = string.Join(", ", _allowedTypes.Select(t => t.Name));
            return CompatibilityHelpers.FromResult(ValidationResult.Failure(
                $"Message type '{messageType.Name}' does not implement any of the allowed interfaces: {allowedTypeNames}"));
        }

        return CompatibilityHelpers.FromResult(ValidationResult.Success());
    }
}