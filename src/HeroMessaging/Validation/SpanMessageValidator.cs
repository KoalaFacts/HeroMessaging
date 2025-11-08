using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using System;

namespace HeroMessaging.Validation;

/// <summary>
/// Example: Zero-allocation message validator using ref struct with interface implementation.
/// Demonstrates C# 13's ref struct interfaces feature for high-performance validation.
/// </summary>
/// <typeparam name="T">The message type to validate</typeparam>
public ref struct SpanMessageValidator<T> : ISpanValidator<T> where T : IMessage
{
    private readonly bool _requireMessageId;
    private readonly bool _requireCorrelationId;
    private readonly int _maxContentLength;

    public SpanMessageValidator(
        bool requireMessageId = true,
        bool requireCorrelationId = false,
        int maxContentLength = 1_000_000)
    {
        _requireMessageId = requireMessageId;
        _requireCorrelationId = requireCorrelationId;
        _maxContentLength = maxContentLength;
    }

    public int MaxErrors => 3; // MessageId, CorrelationId, ContentLength

    public bool Validate(T message, Span<string> errors, out int errorCount)
    {
        errorCount = 0;

        if (_requireMessageId && string.IsNullOrWhiteSpace(message.MessageId))
        {
            if (errorCount < errors.Length)
                errors[errorCount] = "MessageId is required";
            errorCount++;
        }

        if (_requireCorrelationId && string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            if (errorCount < errors.Length)
                errors[errorCount] = "CorrelationId is required";
            errorCount++;
        }

        // Example: validate message content length if it's a string-based message
        if (message is IMessage<string> stringMessage)
        {
            var content = stringMessage.ToString();
            if (content != null && content.Length > _maxContentLength)
            {
                if (errorCount < errors.Length)
                    errors[errorCount] = $"Message content exceeds maximum length of {_maxContentLength}";
                errorCount++;
            }
        }

        return errorCount == 0;
    }
}

/// <summary>
/// Example: Composite ref struct validator that chains multiple validators.
/// Demonstrates zero-allocation validator composition.
/// </summary>
/// <typeparam name="T">The message type</typeparam>
public ref struct CompositeSpanValidator<T> : ISpanValidator<T> where T : IMessage
{
    private readonly ReadOnlySpan<ISpanValidator<T>> _validators;

    public CompositeSpanValidator(ReadOnlySpan<ISpanValidator<T>> validators)
    {
        _validators = validators;
    }

    public int MaxErrors
    {
        get
        {
            int total = 0;
            foreach (var validator in _validators)
            {
                total += validator.MaxErrors;
            }
            return total;
        }
    }

    public bool Validate(T message, Span<string> errors, out int errorCount)
    {
        errorCount = 0;
        int offset = 0;

        foreach (var validator in _validators)
        {
            var remaining = errors.Slice(offset);
            if (validator.Validate(message, remaining, out var validatorErrors))
            {
                continue; // Validator passed
            }

            errorCount += validatorErrors;
            offset += validatorErrors;

            if (offset >= errors.Length)
                break; // Error buffer full
        }

        return errorCount == 0;
    }
}
