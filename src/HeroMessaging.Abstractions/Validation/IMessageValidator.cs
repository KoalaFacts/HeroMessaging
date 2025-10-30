using HeroMessaging.Abstractions.Messages;
using System.Collections.Immutable;

namespace HeroMessaging.Abstractions.Validation;

/// <summary>
/// Interface for validating messages before they are processed by the messaging system.
/// Ensures message integrity, business rule compliance, and data consistency.
/// </summary>
/// <remarks>
/// Implement this interface to create custom message validators that integrate with HeroMessaging.
/// Validators are executed before message processing to catch errors early and provide
/// clear feedback to callers.
///
/// Design Principles:
/// - Fail fast: Validate before expensive operations (database, network)
/// - Clear errors: Provide specific, actionable error messages
/// - High performance: Complete validation in &lt;1ms for typical messages
/// - Composable: Chain multiple validators together
/// - Stateless: Validators should be thread-safe and reusable
///
/// Common validation scenarios:
/// - Required field validation (non-null, non-empty)
/// - Format validation (email, phone, URL patterns)
/// - Range validation (min/max values, string lengths)
/// - Business rule validation (order total matches line items, etc.)
/// - Reference validation (foreign keys exist, user has permissions)
/// - Cross-field validation (start date before end date, etc.)
///
/// <code>
/// public class OrderCommandValidator : IMessageValidator
/// {
///     private readonly IOrderRepository _repository;
///
///     public OrderCommandValidator(IOrderRepository repository)
///     {
///         _repository = repository;
///     }
///
///     public async ValueTask&lt;ValidationResult&gt; ValidateAsync(
///         IMessage message,
///         CancellationToken cancellationToken)
///     {
///         if (message is not CreateOrderCommand cmd)
///             return ValidationResult.Success();
///
///         var errors = new List&lt;string&gt;();
///
///         // Required fields
///         if (string.IsNullOrWhiteSpace(cmd.CustomerId))
///             errors.Add("CustomerId is required");
///
///         // Range validation
///         if (cmd.Amount &lt;= 0)
///             errors.Add("Amount must be greater than zero");
///
///         // Reference validation
///         if (!await _repository.CustomerExistsAsync(cmd.CustomerId, cancellationToken))
///             errors.Add($"Customer '{cmd.CustomerId}' not found");
///
///         return errors.Count > 0
///             ? ValidationResult.Failure(errors.ToArray())
///             : ValidationResult.Success();
///     }
/// }
/// </code>
///
/// Integration with HeroMessaging:
/// <code>
/// // Register validator in DI
/// services.AddScoped&lt;IMessageValidator, OrderCommandValidator&gt;();
///
/// // Framework automatically calls validators before processing
/// await messaging.Send(new CreateOrderCommand("CUST-001", -50)); // Throws ValidationException
/// </code>
/// </remarks>
public interface IMessageValidator
{
    /// <summary>
    /// Validates a message and returns a result indicating success or failure with error details.
    /// </summary>
    /// <param name="message">The message to validate. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the validation operation.</param>
    /// <returns>
    /// A ValueTask containing a ValidationResult.
    /// If valid: IsValid = true, Errors is empty.
    /// If invalid: IsValid = false, Errors contains specific error messages.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
    /// <remarks>
    /// This method should:
    /// - Perform all validation checks synchronously or asynchronously
    /// - Return success if validation passes
    /// - Return failure with specific error messages if validation fails
    /// - NOT throw exceptions for validation failures (use ValidationResult.Failure)
    /// - Only throw exceptions for catastrophic errors (database down, etc.)
    /// - Complete in &lt;1ms for simple validations, &lt;10ms for database validations
    ///
    /// Validation error messages should be:
    /// - Specific: "CustomerId is required" not "Invalid data"
    /// - Actionable: Tell the user what to fix
    /// - User-friendly: Avoid technical jargon when possible
    /// - Consistent: Use similar phrasing across validators
    ///
    /// Performance considerations:
    /// - Validate simple rules first (null checks, ranges) before expensive operations
    /// - Cache reference data when possible (user permissions, static lists)
    /// - Use async I/O for database/network validation
    /// - Consider batching reference validations (validate all IDs in one query)
    /// - Avoid N+1 queries when validating collections
    ///
    /// <code>
    /// // Validator implementation
    /// public async ValueTask&lt;ValidationResult&gt; ValidateAsync(
    ///     IMessage message,
    ///     CancellationToken cancellationToken)
    /// {
    ///     if (message is not OrderCommand order)
    ///         return ValidationResult.Success(); // Not applicable
    ///
    ///     var errors = new List&lt;string&gt;();
    ///
    ///     // Simple validation first
    ///     if (string.IsNullOrEmpty(order.CustomerId))
    ///         errors.Add("Customer ID is required");
    ///
    ///     if (order.Amount &lt;= 0)
    ///         errors.Add("Amount must be positive");
    ///
    ///     // Early return if basic validation fails
    ///     if (errors.Count > 0)
    ///         return ValidationResult.Failure(errors.ToArray());
    ///
    ///     // Expensive validation only if needed
    ///     if (!await _repository.ExistsAsync(order.CustomerId, cancellationToken))
    ///         errors.Add($"Customer {order.CustomerId} not found");
    ///
    ///     return errors.Count > 0
    ///         ? ValidationResult.Failure(errors.ToArray())
    ///         : ValidationResult.Success();
    /// }
    ///
    /// // Usage in application
    /// var validator = serviceProvider.GetRequiredService&lt;IMessageValidator&gt;();
    /// var result = await validator.ValidateAsync(message, cancellationToken);
    ///
    /// if (!result.IsValid)
    /// {
    ///     // Handle validation errors
    ///     foreach (var error in result.Errors)
    ///     {
    ///         Console.WriteLine($"Validation error: {error}");
    ///     }
    ///     throw new ValidationException(result.Errors.ToArray());
    /// }
    /// </code>
    /// </remarks>
    ValueTask<ValidationResult> ValidateAsync(IMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a message validation operation.
/// Implemented as a readonly record struct to minimize heap allocations and maximize performance.
/// </summary>
/// <remarks>
/// ValidationResult is designed for high-performance validation scenarios:
/// - Implemented as struct (stack allocated, zero GC pressure)
/// - Immutable via record struct semantics
/// - Factory methods for creating success/failure results
/// - Uses ImmutableArray for error collection (thread-safe, efficient)
///
/// Use this type instead of throwing exceptions for validation failures:
/// - Validation failures are expected and should not use exceptions
/// - Explicit success/failure makes code flow clearer
/// - Enables collecting all validation errors (not just first failure)
/// - Better performance than exception-based validation
///
/// <code>
/// // Success case
/// var result = ValidationResult.Success();
///
/// // Failure with single error
/// var result = ValidationResult.Failure("Customer ID is required");
///
/// // Failure with multiple errors
/// var result = ValidationResult.Failure(
///     "Customer ID is required",
///     "Amount must be greater than zero"
/// );
///
/// // Check result
/// if (!result.IsValid)
/// {
///     foreach (var error in result.Errors)
///     {
///         logger.LogWarning("Validation error: {Error}", error);
///     }
/// }
/// </code>
/// </remarks>
public readonly record struct ValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation succeeded.
    /// True if valid, false if validation errors were found.
    /// </summary>
    /// <remarks>
    /// Always check this property before accessing Errors:
    /// - If true: Errors is empty, message is valid
    /// - If false: Errors contains one or more error messages
    ///
    /// <code>
    /// if (!result.IsValid)
    /// {
    ///     // Handle validation errors
    ///     throw new ValidationException(result.Errors.ToArray());
    /// }
    /// </code>
    /// </remarks>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the collection of validation error messages.
    /// Empty if validation succeeded (IsValid = true).
    /// </summary>
    /// <remarks>
    /// Error messages should be:
    /// - Specific and actionable (e.g., "Email address is invalid")
    /// - User-friendly (avoid technical jargon)
    /// - Consistent in format and tone
    /// - Suitable for display in UI or API responses
    ///
    /// Use ImmutableArray for:
    /// - Thread-safe access to errors
    /// - Efficient iteration without allocations
    /// - Structural equality in record struct
    ///
    /// <code>
    /// if (!result.IsValid)
    /// {
    ///     Console.WriteLine($"Validation failed with {result.Errors.Length} error(s):");
    ///     foreach (var error in result.Errors)
    ///     {
    ///         Console.WriteLine($"  - {error}");
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public ImmutableArray<string> Errors { get; init; }

    /// <summary>
    /// Creates a successful validation result with no errors.
    /// </summary>
    /// <returns>A ValidationResult with IsValid = true and empty Errors collection.</returns>
    /// <remarks>
    /// Use this factory method to indicate successful validation:
    ///
    /// <code>
    /// public async ValueTask&lt;ValidationResult&gt; ValidateAsync(IMessage message, CancellationToken ct)
    /// {
    ///     if (message is not OrderCommand order)
    ///         return ValidationResult.Success(); // Not applicable to this validator
    ///
    ///     // Perform validation checks...
    ///     if (AllChecksPassed())
    ///         return ValidationResult.Success();
    ///
    ///     return ValidationResult.Failure("Validation failed");
    /// }
    /// </code>
    /// </remarks>
    public static ValidationResult Success() => new() { IsValid = true, Errors = ImmutableArray<string>.Empty };

    /// <summary>
    /// Creates a failed validation result with one or more error messages.
    /// </summary>
    /// <param name="errors">One or more error messages describing why validation failed. Must not be null or empty.</param>
    /// <returns>A ValidationResult with IsValid = false and the provided error messages.</returns>
    /// <remarks>
    /// Use this factory method to indicate validation failure with specific error messages:
    ///
    /// <code>
    /// // Single error
    /// return ValidationResult.Failure("Customer ID is required");
    ///
    /// // Multiple errors
    /// return ValidationResult.Failure(
    ///     "Customer ID is required",
    ///     "Amount must be greater than zero",
    ///     "Order date cannot be in the future"
    /// );
    ///
    /// // From a list of errors
    /// var errors = new List&lt;string&gt;();
    /// // ... collect errors ...
    /// return ValidationResult.Failure(errors.ToArray());
    /// </code>
    ///
    /// Best practices for error messages:
    /// - Be specific: "Email address is invalid" not "Invalid input"
    /// - Be actionable: Tell user what to fix
    /// - Be professional: Avoid blame ("You entered" vs "The value is")
    /// - Be consistent: Use similar phrasing across validators
    /// </remarks>
    public static ValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToImmutableArray() };
}

/// <summary>
/// Exception thrown when message validation fails during processing.
/// Contains detailed validation error messages for diagnostics and user feedback.
/// </summary>
/// <remarks>
/// This exception is typically thrown by the HeroMessaging framework when:
/// - A message fails validation before processing
/// - Multiple validation rules are violated
/// - Validation errors need to be propagated to the caller
///
/// The exception aggregates all validation errors into a single exception,
/// making it easy to handle validation failures consistently.
///
/// Use cases:
/// - API error responses (return validation errors to client)
/// - Logging and diagnostics (capture all validation failures)
/// - Testing (verify validation rules are enforced)
/// - Error handling middleware (convert to HTTP 400 Bad Request)
///
/// <code>
/// try
/// {
///     await messaging.Send(new CreateOrderCommand(customerId: "", amount: -50));
/// }
/// catch (ValidationException ex)
/// {
///     // Access validation errors
///     foreach (var error in ex.Errors)
///     {
///         Console.WriteLine($"Validation error: {error}");
///     }
///
///     // Return to API caller
///     return BadRequest(new { errors = ex.Errors });
/// }
/// </code>
///
/// Best practices:
/// - Catch ValidationException specifically (not just Exception)
/// - Log all validation errors for diagnostics
/// - Return errors to caller with HTTP 400 (Bad Request)
/// - Don't retry validation failures (they won't succeed without changes)
/// - Use Error property for structured error handling
/// </remarks>
public class ValidationException : Exception
{
    /// <summary>
    /// Gets the collection of validation error messages that caused this exception.
    /// </summary>
    /// <remarks>
    /// This collection contains all validation errors found during message validation.
    /// Each error should be a specific, actionable message suitable for display to users.
    ///
    /// The errors are stored as IReadOnlyList to:
    /// - Prevent modification after exception creation
    /// - Enable efficient iteration
    /// - Support serialization for API responses
    ///
    /// <code>
    /// catch (ValidationException ex)
    /// {
    ///     if (ex.Errors.Count > 0)
    ///     {
    ///         logger.LogWarning("Validation failed with {Count} error(s)", ex.Errors.Count);
    ///         foreach (var error in ex.Errors)
    ///         {
    ///             logger.LogWarning("  - {Error}", error);
    ///         }
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new instance of ValidationException with a default message.
    /// </summary>
    /// <remarks>
    /// Creates a validation exception with no specific errors.
    /// This constructor is primarily for serialization support.
    ///
    /// Prefer using the constructor that accepts errors for meaningful validation failures:
    /// <code>
    /// throw new ValidationException(new[] { "Customer ID is required" });
    /// </code>
    /// </remarks>
    public ValidationException()
        : base("Validation failed")
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with a custom message.
    /// </summary>
    /// <param name="message">The error message that explains the validation failure.</param>
    /// <remarks>
    /// Creates a validation exception with a custom message but no specific errors.
    ///
    /// Use this when you have a single validation failure or a summary message:
    /// <code>
    /// throw new ValidationException("Order validation failed");
    /// </code>
    ///
    /// For multiple validation errors, prefer using the constructor that accepts an errors collection.
    /// </remarks>
    public ValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with a custom message and inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the validation failure.</param>
    /// <param name="innerException">The exception that caused this validation failure.</param>
    /// <remarks>
    /// Use this constructor when a validation failure is caused by another exception:
    /// <code>
    /// try
    /// {
    ///     await ValidateCustomerExistsAsync(customerId);
    /// }
    /// catch (DatabaseException dbEx)
    /// {
    ///     throw new ValidationException("Failed to validate customer", dbEx);
    /// }
    /// </code>
    ///
    /// This preserves the original exception stack trace for debugging.
    /// </remarks>
    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of ValidationException with a collection of validation errors.
    /// This is the recommended constructor for validation failures.
    /// </summary>
    /// <param name="errors">
    /// The collection of validation error messages.
    /// Each message should be specific and actionable.
    /// Must not be null (use empty array for no errors).
    /// </param>
    /// <remarks>
    /// This is the primary constructor for validation failures with specific error messages.
    /// The exception message will be a comma-separated list of all errors.
    ///
    /// <code>
    /// // From ValidationResult
    /// var result = await validator.ValidateAsync(message, cancellationToken);
    /// if (!result.IsValid)
    /// {
    ///     throw new ValidationException(result.Errors.ToArray());
    /// }
    ///
    /// // Manually constructed errors
    /// var errors = new List&lt;string&gt;
    /// {
    ///     "Customer ID is required",
    ///     "Amount must be greater than zero",
    ///     "Order date cannot be in the future"
    /// };
    /// throw new ValidationException(errors);
    ///
    /// // Single error
    /// throw new ValidationException(new[] { "Invalid order state" });
    /// </code>
    ///
    /// The exception message format is:
    /// "Validation failed: {error1}, {error2}, {error3}"
    ///
    /// This makes the exception message informative even when logged without accessing the Errors property.
    /// </remarks>
    public ValidationException(IReadOnlyList<string> errors)
        : base($"Validation failed: {string.Join(", ", errors)}")
    {
        Errors = errors ?? Array.Empty<string>();
    }
}