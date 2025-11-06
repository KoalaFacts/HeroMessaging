using System.Security.Claims;

namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Represents the security context for message operations
/// </summary>
public sealed class SecurityContext
{
    /// <summary>
    /// The authenticated principal (user/service identity)
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Message identifier for audit trail
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// Message type for authorization decisions
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// Correlation identifier for distributed tracing
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional metadata for security operations
    /// </summary>
    public IDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Timestamp when context was created
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    public SecurityContext()
    {
        Metadata = new Dictionary<string, object>();
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets whether the context has an authenticated principal
    /// </summary>
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Gets the principal's name if authenticated
    /// </summary>
    public string? PrincipalName => Principal?.Identity?.Name;

    /// <summary>
    /// Checks if the principal has a specific claim
    /// </summary>
    public bool HasClaim(string claimType, string? claimValue = null)
    {
        if (Principal == null) return false;

        return claimValue == null
            ? Principal.HasClaim(c => c.Type == claimType)
            : Principal.HasClaim(claimType, claimValue);
    }

    /// <summary>
    /// Gets all claims of a specific type
    /// </summary>
    public IEnumerable<Claim> GetClaims(string claimType)
    {
        return Principal?.FindAll(claimType) ?? Enumerable.Empty<Claim>();
    }
}
