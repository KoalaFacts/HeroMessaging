using System.Security.Claims;

namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides authorization services for message handling
/// </summary>
public interface IAuthorizationProvider
{
    /// <summary>
    /// Authorizes a message operation
    /// </summary>
    /// <param name="principal">The authenticated principal</param>
    /// <param name="messageType">The type of message being processed</param>
    /// <param name="operation">The operation being performed (Send, Receive, Handle)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authorization result</returns>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal principal,
        string messageType,
        string operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a principal has a specific permission
    /// </summary>
    /// <param name="principal">The authenticated principal</param>
    /// <param name="permission">The permission to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if authorized, false otherwise</returns>
    Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        string permission,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an authorization check
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Whether authorization succeeded
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Error message if authorization failed
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Reason for failure (e.g., "InsufficientPermissions", "ResourceNotFound")
    /// </summary>
    public string? FailureReason { get; }

    private AuthorizationResult(bool succeeded, string? errorMessage, string? failureReason)
    {
        Succeeded = succeeded;
        ErrorMessage = errorMessage;
        FailureReason = failureReason;
    }

    public static AuthorizationResult Success()
        => new(true, null, null);

    public static AuthorizationResult Failure(string errorMessage, string? failureReason = null)
        => new(false, errorMessage, failureReason);

    public static AuthorizationResult InsufficientPermissions(string requiredPermission)
        => new(false, $"Insufficient permissions. Required: {requiredPermission}", "InsufficientPermissions");

    public static AuthorizationResult Forbidden(string message)
        => new(false, message, "Forbidden");
}

/// <summary>
/// Standard message operations for authorization.
/// </summary>
public static class MessageOperations
{
    /// <summary>
    /// Operation type for sending point-to-point messages.
    /// </summary>
    public const string Send = "Send";

    /// <summary>
    /// Operation type for receiving messages.
    /// </summary>
    public const string Receive = "Receive";

    /// <summary>
    /// Operation type for handling/processing messages.
    /// </summary>
    public const string Handle = "Handle";

    /// <summary>
    /// Operation type for publishing events.
    /// </summary>
    public const string Publish = "Publish";

    /// <summary>
    /// Operation type for subscribing to events.
    /// </summary>
    public const string Subscribe = "Subscribe";
}
