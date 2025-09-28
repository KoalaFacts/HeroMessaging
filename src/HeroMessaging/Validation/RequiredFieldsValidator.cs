using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;
using System.Reflection;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that required fields in a message are populated
/// </summary>
public class RequiredFieldsValidator : IMessageValidator
{
    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var type = message.GetType();

        // Check MessageId
        if (message.MessageId == Guid.Empty)
        {
            errors.Add("MessageId is required and cannot be empty");
        }

        // Check Timestamp
        if (message.Timestamp == default)
        {
            errors.Add("Timestamp is required and cannot be default");
        }

        // Check for properties marked with Required attribute (if any)
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var requiredAttr = property.GetCustomAttribute<RequiredAttribute>();
            if (requiredAttr != null)
            {
                var value = property.GetValue(message);
                if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    errors.Add($"Property '{property.Name}' is required but was not provided");
                }
            }
        }

        return CompatibilityHelpers.FromResult(errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success());
    }
}

/// <summary>
/// Attribute to mark properties as required for validation
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RequiredAttribute : Attribute
{
}