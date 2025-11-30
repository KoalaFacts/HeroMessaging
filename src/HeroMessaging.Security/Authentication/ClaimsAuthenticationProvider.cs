using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Authentication;

/// <summary>
/// Provides claims-based authentication for messages.
/// Thread-safe for concurrent registration and authentication operations.
/// </summary>
public sealed class ClaimsAuthenticationProvider : IAuthenticationProvider
{
    private readonly ConcurrentDictionary<string, ClaimsPrincipal> _apiKeys;

    public string Scheme { get; }

    /// <summary>
    /// Creates a new claims authentication provider
    /// </summary>
    /// <param name="scheme">Authentication scheme name (e.g., "ApiKey", "Bearer")</param>
    public ClaimsAuthenticationProvider(string scheme = "ApiKey")
    {
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        _apiKeys = new ConcurrentDictionary<string, ClaimsPrincipal>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Registers an API key with associated claims
    /// </summary>
    public void RegisterApiKey(string apiKey, ClaimsPrincipal principal)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));
        _apiKeys[apiKey] = principal ?? throw new ArgumentNullException(nameof(principal));
    }

    /// <summary>
    /// Registers an API key with a simple identity
    /// </summary>
    public void RegisterApiKey(string apiKey, string name, params ReadOnlySpan<Claim> claims)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        var identity = new ClaimsIdentity(claims.ToArray(), Scheme, ClaimTypes.Name, ClaimTypes.Role);
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

        if (credentials.Scheme != Scheme)
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
