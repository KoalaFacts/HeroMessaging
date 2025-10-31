using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Validation;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that validates messages before processing
/// </summary>
public class ValidationDecorator(
    IMessageProcessor inner,
    IMessageValidator validator,
    ILogger<ValidationDecorator> logger) : MessageProcessorDecorator(inner)
{
    private readonly IMessageValidator _validator = validator;
    private readonly ILogger<ValidationDecorator> _logger = logger;

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(message, cancellationToken);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Message {MessageId} failed validation: {Errors}",
                message.MessageId, string.Join(", ", validationResult.Errors));

            return ProcessingResult.Failed(
                new ValidationException(validationResult.Errors),
                $"Validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        return await _inner.ProcessAsync(message, context, cancellationToken);
    }
}

/// <summary>
/// Composite validator that combines multiple validators and aggregates their validation results
/// </summary>
/// <remarks>
/// This validator implements the Composite pattern to execute multiple validators in sequence
/// and collect all validation errors. Useful for applying different validation rules (e.g., schema
/// validation, business rules, security checks) to a message without short-circuiting on first failure.
///
/// Validation behavior:
/// - All validators are executed regardless of individual failures
/// - Validation errors from all validators are aggregated into a single result
/// - Validation succeeds only if all validators succeed
/// - Validators are executed in the order they were added
///
/// Performance considerations:
/// - All validators execute even if early validators fail
/// - For fail-fast behavior, use individual validators instead
/// - Validator execution is sequential (not parallel)
///
/// Example usage:
/// <code>
/// var compositeValidator = new CompositeValidator(
///     new MessageTypeValidator(),
///     new MessageSizeValidator(maxSize: 1024 * 1024),
///     new RequiredFieldsValidator(),
///     new BusinessRuleValidator()
/// );
///
/// var result = await compositeValidator.ValidateAsync(message);
/// if (!result.IsValid)
/// {
///     // All validation errors from all validators
///     foreach (var error in result.Errors)
///     {
///         Console.WriteLine(error);
///     }
/// }
/// </code>
/// </remarks>
public class CompositeValidator : IMessageValidator
{
    private readonly List<IMessageValidator> _validators = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeValidator"/> class with specified validators
    /// </summary>
    /// <param name="validators">The validators to combine</param>
    public CompositeValidator(params IMessageValidator[] validators)
    {
        _validators.AddRange(validators);
    }

    public async ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            if (!result.IsValid)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Any()
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }
}