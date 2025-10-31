using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Validation;
using HeroMessaging.Utilities;
using System.Text.Json;

namespace HeroMessaging.Validation;

/// <summary>
/// Validates that message size doesn't exceed a configurable maximum limit when serialized to JSON.
/// Prevents oversized messages from consuming excessive resources or causing transport failures.
/// </summary>
/// <remarks>
/// This validator enforces message size limits by serializing messages to JSON and measuring
/// the resulting byte count. It helps prevent memory exhaustion, network timeouts, and
/// performance degradation caused by excessively large messages.
///
/// Validation Rules:
/// - Message is serialized to JSON using System.Text.Json
/// - Serialized size is measured in UTF-8 bytes
/// - Size must not exceed the configured maximum (default: 1MB)
/// - Validation fails with descriptive error if size exceeds limit
///
/// Default Size Limit:
/// - 1 MB (1,048,576 bytes) - suitable for most messaging scenarios
/// - Configure larger limits for bulk data transfers
/// - Configure smaller limits for memory-constrained environments
///
/// Size Calculation:
/// The validator serializes the entire message object graph to JSON and counts UTF-8 bytes.
/// This includes all properties, nested objects, and collections. The size measured is
/// approximately the size that would be transmitted over the wire.
///
/// Performance Considerations:
/// - Serialization has measurable overhead (typically 1-10ms depending on message complexity)
/// - Large messages (>100KB) can take 10-50ms to serialize
/// - Consider the tradeoff between validation overhead and preventing oversized messages
/// - For high-throughput scenarios, consider sampling or async validation
/// - Serialization allocates memory (size roughly equal to message size)
///
/// Common Size Guidelines:
/// - Simple commands/events: &lt;1KB
/// - Typical business messages: 1-10KB
/// - Messages with collections: 10-100KB
/// - Bulk data transfers: 100KB-1MB
/// - Large payloads (>1MB): Consider using claim check pattern
///
/// <code>
/// // Default configuration - 1MB limit
/// var validator = new MessageSizeValidator();
/// var result = await validator.ValidateAsync(message, ct);
///
/// // Custom size limit - 100KB for memory-constrained environment
/// var smallValidator = new MessageSizeValidator(maxSizeInBytes: 100 * 1024);
///
/// // Large size limit - 10MB for bulk data transfers
/// var largeValidator = new MessageSizeValidator(maxSizeInBytes: 10 * 1024 * 1024);
/// </code>
///
/// Integration with HeroMessaging:
/// <code>
/// // Register with default 1MB limit
/// services.AddSingleton&lt;IMessageValidator&gt;(
///     new MessageSizeValidator()
/// );
///
/// // Register with custom limit based on environment
/// services.AddSingleton&lt;IMessageValidator&gt;(
///     new MessageSizeValidator(maxSizeInBytes: configuration.GetValue&lt;int&gt;("MaxMessageSizeBytes"))
/// );
///
/// // Validator runs automatically in the pipeline
/// await messaging.Send(new CreateOrderCommand()); // Size validated before processing
/// </code>
///
/// Error Handling:
/// - Oversized messages: Returns validation failure with size details
/// - Serialization errors: Returns validation failure with error message
/// - Does not throw exceptions (errors returned via ValidationResult)
///
/// Claim Check Pattern:
/// For messages that legitimately need to transfer large data:
/// <code>
/// // Instead of embedding large data in message
/// public class ProcessLargeFileCommand : ICommand
/// {
///     public string FileData { get; set; } // DON'T: Inline large data
/// }
///
/// // Use claim check pattern
/// public class ProcessLargeFileCommand : ICommand
/// {
///     public string FileStorageUri { get; set; } // DO: Reference to stored data
///     public string FileChecksum { get; set; }
///     public long FileSizeBytes { get; set; }
/// }
/// // Store large data separately (blob storage, file system, etc.)
/// // Message only contains reference and metadata
/// </code>
/// </remarks>
/// <param name="maxSizeInBytes">
/// Maximum allowed message size in bytes when serialized to JSON.
/// Default is 1,048,576 bytes (1 MB).
/// Must be positive. Consider your infrastructure limits when configuring.
/// </param>
public class MessageSizeValidator(int maxSizeInBytes = 1024 * 1024) : IMessageValidator
{
    private readonly int _maxSizeInBytes = maxSizeInBytes;


    /// <summary>
    /// Validates that the message size doesn't exceed the configured maximum when serialized to JSON.
    /// </summary>
    /// <param name="message">The message to validate. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the validation operation.</param>
    /// <returns>
    /// A ValueTask containing a ValidationResult.
    /// Success if message size is within limits.
    /// Failure if message size exceeds limit or serialization fails, with specific error details.
    /// </returns>
    /// <remarks>
    /// This method performs size validation by:
    /// 1. Serializing the message to JSON using System.Text.Json
    /// 2. Counting UTF-8 bytes in the serialized JSON
    /// 3. Comparing byte count to configured maximum
    /// 4. Returning success or failure with size details
    ///
    /// The validation is synchronous and returns a completed ValueTask.
    /// Serialization happens on the calling thread and may block for large messages.
    ///
    /// Serialization Behavior:
    /// - Uses default System.Text.Json options
    /// - Includes all public properties
    /// - Follows standard JSON serialization rules
    /// - Circular references will cause serialization failure
    /// - Non-serializable types will cause serialization failure
    ///
    /// Performance Impact:
    /// - Small messages (&lt;1KB): &lt;1ms overhead
    /// - Medium messages (1-10KB): 1-5ms overhead
    /// - Large messages (10-100KB): 5-20ms overhead
    /// - Very large messages (>100KB): 20-50ms+ overhead
    /// - Memory allocation approximately equal to message size
    ///
    /// Error Scenarios:
    /// - Message too large: "Message size X bytes exceeds maximum allowed size of Y bytes"
    /// - Serialization fails: "Failed to validate message size: {exception message}"
    /// - No exceptions are thrown (errors returned in ValidationResult)
    ///
    /// <code>
    /// var validator = new MessageSizeValidator(maxSizeInBytes: 1024); // 1KB limit
    ///
    /// // Small message - validation succeeds
    /// var smallCommand = new CreateOrderCommand
    /// {
    ///     MessageId = Guid.NewGuid(),
    ///     Timestamp = DateTimeOffset.UtcNow,
    ///     CustomerId = "CUST-001",
    ///     Amount = 99.99m
    /// };
    /// var result = await validator.ValidateAsync(smallCommand, ct);
    /// // result.IsValid == true (message is ~200 bytes)
    ///
    /// // Large message - validation fails
    /// var largeCommand = new ImportDataCommand
    /// {
    ///     MessageId = Guid.NewGuid(),
    ///     Timestamp = DateTimeOffset.UtcNow,
    ///     Data = new string('x', 10000) // 10KB of data
    /// };
    /// var result = await validator.ValidateAsync(largeCommand, ct);
    /// // result.IsValid == false
    /// // result.Errors[0] == "Message size 10123 bytes exceeds maximum allowed size of 1024 bytes"
    /// </code>
    ///
    /// Best Practices:
    /// - Set size limits based on your infrastructure capabilities
    /// - Consider transport limits (e.g., HTTP max request size, message queue limits)
    /// - Monitor message sizes in production to tune limits
    /// - Use claim check pattern for legitimately large data
    /// - Log oversized messages for analysis
    /// - Consider compression for large but compressible data
    ///
    /// Troubleshooting Oversized Messages:
    /// If messages are too large:
    /// - Remove unnecessary data from message payload
    /// - Use claim check pattern (store data separately, send reference)
    /// - Split into multiple smaller messages
    /// - Compress data before including in message
    /// - Review data models for optimization opportunities
    /// </remarks>
    public ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(json);

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