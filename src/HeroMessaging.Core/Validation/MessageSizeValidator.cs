using System.Text.Json;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;

namespace HeroMessaging.Core.Validation;

/// <summary>
/// Validates that message size doesn't exceed a maximum limit
/// </summary>
public class MessageSizeValidator : IMessageValidator
{
    private readonly int _maxSizeInBytes;
    
    public MessageSizeValidator(int maxSizeInBytes = 1024 * 1024) // Default 1MB
    {
        _maxSizeInBytes = maxSizeInBytes;
    }
    
    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(json);
            
            if (sizeInBytes > _maxSizeInBytes)
            {
                return ValueTask.FromResult(ValidationResult.Failure(
                    $"Message size {sizeInBytes} bytes exceeds maximum allowed size of {_maxSizeInBytes} bytes"));
            }
            
            return ValueTask.FromResult(ValidationResult.Success());
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(ValidationResult.Failure($"Failed to validate message size: {ex.Message}"));
        }
    }
}