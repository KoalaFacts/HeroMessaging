using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that messages implement the correct interfaces based on their type
/// </summary>
public class MessageTypeValidator : IMessageValidator
{
    private readonly HashSet<Type> _allowedTypes;

    public MessageTypeValidator(params IEnumerable<Type> allowedTypes)
    {
        _allowedTypes = allowedTypes?.Any() == true
            ? [.. allowedTypes]
            : [typeof(ICommand), typeof(IEvent)];
    }

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
