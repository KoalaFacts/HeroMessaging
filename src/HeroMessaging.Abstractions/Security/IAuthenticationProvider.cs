using System.Security.Claims;

namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides authentication services for message senders
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Authenticates a message sender based on credentials or tokens
    /// </summary>
    /// <param name="credentials">The authentication credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated principal if successful, null otherwise</returns>
    Task<ClaimsPrincipal?> AuthenticateAsync(
        AuthenticationCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an authentication token
    /// </summary>
    /// <param name="token">The token to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authenticated principal if token is valid, null otherwise</returns>
    Task<ClaimsPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the authentication scheme name
    /// </summary>
    string Scheme { get; }
}

/// <summary>
/// Represents authentication credentials
/// </summary>
public sealed class AuthenticationCredentials
{
    /// <summary>
    /// Authentication scheme (e.g., "Bearer", "ApiKey", "Basic")
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// The credential value (token, key, password, etc.)
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Additional parameters for authentication
    /// </summary>
    public IDictionary<string, string> Parameters { get; }

    public AuthenticationCredentials(string scheme, string value)
    {
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Parameters = new Dictionary<string, string>();
    }

    /// <summary>
    /// Creates credentials from an authorization header value
    /// </summary>
    public static AuthenticationCredentials FromAuthorizationHeader(string headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            throw new ArgumentException("Header value cannot be empty", nameof(headerValue));

        var parts = headerValue.Split(new[] { ' ' }, 2);
        if (parts.Length != 2)
            throw new ArgumentException("Invalid authorization header format", nameof(headerValue));

        return new AuthenticationCredentials(parts[0], parts[1]);
    }
}

/// <summary>
/// Result of an authentication operation
/// </summary>
public sealed class AuthenticationResult
{
    /// <summary>
    /// Whether authentication succeeded
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// The authenticated principal if successful
    /// </summary>
    public ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    public string? ErrorMessage { get; }

    private AuthenticationResult(bool succeeded, ClaimsPrincipal? principal, string? errorMessage)
    {
        Succeeded = succeeded;
        Principal = principal;
        ErrorMessage = errorMessage;
    }

    public static AuthenticationResult Success(ClaimsPrincipal principal)
        => new(true, principal, null);

    public static AuthenticationResult Failure(string errorMessage)
        => new(false, null, errorMessage);
}
