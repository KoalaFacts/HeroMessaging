using System.Collections.Immutable;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Core.Processing.Decorators;

/// <summary>
/// Decorator that validates messages before processing
/// </summary>
public class ValidationDecorator : MessageProcessorDecorator
{
    private readonly IMessageValidator _validator;
    private readonly ILogger<ValidationDecorator> _logger;

    public ValidationDecorator(
        IMessageProcessor inner,
        IMessageValidator validator,
        ILogger<ValidationDecorator> logger) : base(inner)
    {
        _validator = validator;
        _logger = logger;
    }

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

public interface IMessageValidator
{
    ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default);
}

public readonly record struct ValidationResult
{
    public bool IsValid { get; init; }
    public ImmutableArray<string> Errors { get; init; }
    
    public static ValidationResult Success() => new() { IsValid = true, Errors = ImmutableArray<string>.Empty };
    public static ValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToImmutableArray() };
}

public class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    
    public ValidationException(IReadOnlyList<string> errors) : base($"Validation failed: {string.Join(", ", errors)}")
    {
        Errors = errors;
    }
}

public class CompositeValidator : IMessageValidator
{
    private readonly List<IMessageValidator> _validators = new();
    
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