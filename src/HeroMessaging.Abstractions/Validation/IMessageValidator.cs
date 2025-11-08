using System.Collections.Immutable;
using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Validation;

/// <summary>
/// Validates messages before processing
/// </summary>
public interface IMessageValidator
{
    /// <summary>
    /// Validates a message
    /// </summary>
    /// <param name="message">The message to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with success flag and any errors</returns>
    ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of message validation
/// </summary>
public readonly record struct ValidationResult
{
    /// <summary>
    /// Whether the validation was successful
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Collection of validation errors if any
    /// </summary>
    public ImmutableArray<string> Errors { get; init; }

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true, Errors = ImmutableArray<string>.Empty };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static ValidationResult Failure(params ReadOnlySpan<string> errors) => new() { IsValid = false, Errors = [.. errors] };
}

/// <summary>
/// Exception thrown when message validation fails
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// Gets the validation errors
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new instance of ValidationException
    /// </summary>
    public ValidationException()
        : base("Validation failed")
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with a message
    /// </summary>
    public ValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with a message and inner exception
    /// </summary>
    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with validation errors
    /// </summary>
    public ValidationException(IReadOnlyList<string> errors)
        : base($"Validation failed: {string.Join(", ", errors)}")
    {
        Errors = errors ?? Array.Empty<string>();
    }
}