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
        var validationResult = await _validator.ValidateAsync(message, cancellationToken).ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Message {MessageId} failed validation: {Errors}",
                message.MessageId, string.Join(", ", validationResult.Errors));

            return ProcessingResult.Failed(
                new ValidationException(validationResult.Errors),
                $"Validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        return await _inner.ProcessAsync(message, context, cancellationToken).ConfigureAwait(false);
    }
}

public class CompositeValidator : IMessageValidator
{
    private readonly List<IMessageValidator> _validators = [];

    public CompositeValidator(params IMessageValidator[] validators)
    {
        _validators.AddRange(validators);
    }

    public async ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken).ConfigureAwait(false);
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
