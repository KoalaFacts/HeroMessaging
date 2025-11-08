using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;
using System;
using System.Buffers;
using System.Text.Json;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that message size doesn't exceed a maximum limit
/// </summary>
public class MessageSizeValidator(int maxSizeInBytes = 1024 * 1024) : IMessageValidator
{
    private readonly int _maxSizeInBytes = maxSizeInBytes;


    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use span-based helper for zero-allocation byte count
            var sizeInBytes = JsonSerializationHelper.GetJsonByteCount(message);

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