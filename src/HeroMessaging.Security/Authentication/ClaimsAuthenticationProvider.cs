using System.Security.Claims;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Authentication;

/// <summary>
/// Provides claims-based authentication for messages
/// </summary>
public sealed class ClaimsAuthenticationProvider : IAuthenticationProvider
{
    private readonly Dictionary<string, ClaimsPrincipal> _apiKeys;
    private readonly string _scheme;

    public string Scheme => _scheme;

    /// <summary>
    /// Creates a new claims authentication provider
    /// </summary>
    /// <param name="scheme">Authentication scheme name (e.g., "ApiKey", "Bearer")</param>
    public ClaimsAuthenticationProvider(string scheme = "ApiKey")
    {
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        _apiKeys = new Dictionary<string, ClaimsPrincipal>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Registers an API key with associated claims
    /// </summary>
    public void RegisterApiKey(string apiKey, ClaimsPrincipal principal)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        if (principal == null)
            throw new ArgumentNullException(nameof(principal));

        _apiKeys[apiKey] = principal;
    }

    /// <summary>
    /// Registers an API key with a simple identity
    /// </summary>
    public void RegisterApiKey(string apiKey, string name, params Claim[] claims)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        var identity = new ClaimsIdentity(claims, _scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, name));

        var principal = new ClaimsPrincipal(identity);
        _apiKeys[apiKey] = principal;
    }

    public Task<ClaimsPrincipal?> AuthenticateAsync(
        AuthenticationCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (credentials == null)
            throw new ArgumentNullException(nameof(credentials));

        if (credentials.Scheme != _scheme)
            return Task.FromResult<ClaimsPrincipal?>(null);

        if (_apiKeys.TryGetValue(credentials.Value, out var principal))
        {
            return Task.FromResult<ClaimsPrincipal?>(principal);
        }

        return Task.FromResult<ClaimsPrincipal?>(null);
    }

    public Task<ClaimsPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<ClaimsPrincipal?>(null);

        if (_apiKeys.TryGetValue(token, out var principal))
        {
            return Task.FromResult<ClaimsPrincipal?>(principal);
        }

        return Task.FromResult<ClaimsPrincipal?>(null);
    }
}
