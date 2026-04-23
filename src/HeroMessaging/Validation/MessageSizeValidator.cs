using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that message size doesn't exceed a maximum limit
/// </summary>
public class MessageSizeValidator : IMessageValidator
{
    private readonly int _maxSizeInBytes;
    private readonly IJsonSerializer _jsonSerializer;
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageSizeValidator"/> class.
    /// </summary>

    public MessageSizeValidator(int maxSizeInBytes, IJsonSerializer jsonSerializer)
    {
        _maxSizeInBytes = maxSizeInBytes;
        _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageSizeValidator"/> class.
    /// </summary>

    public MessageSizeValidator(IJsonSerializer jsonSerializer)
        : this(1024 * 1024, jsonSerializer)
    {
    }
    /// <summary>
    /// Executes validate async.
    /// </summary>

    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use span-based serializer for zero-allocation byte count
            var sizeInBytes = _jsonSerializer.GetJsonByteCount(message);

            if (sizeInBytes > _maxSizeInBytes)
            {
                return CompatibilityHelpers.FromResult(ValidationResult.Failure(
                    $"Message size {sizeInBytes} bytes exceeds maximum allowed size of {_maxSizeInBytes} bytes"));
            }

            return CompatibilityHelpers.FromResult(ValidationResult.Success());
        }
        catch (Exception ex)
        {
            return CompatibilityHelpers.FromResult(ValidationResult.Failure($"Failed to validate message size: {ex.Message}"));
        }
    }
}
