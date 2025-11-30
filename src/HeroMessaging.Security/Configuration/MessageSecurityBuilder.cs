using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Security;

/// <summary>
/// Builder for configuring message security
/// </summary>
public sealed class MessageSecurityBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Creates a new message security builder
    /// </summary>
    public MessageSecurityBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Adds AES-256-GCM encryption
    /// </summary>
    public MessageSecurityBuilder WithAesGcmEncryption(byte[] key, string? keyId = null)
    {
        _services.AddAesGcmEncryption(key, keyId);
        return this;
    }

    /// <summary>
    /// Adds AES-256-GCM encryption with random key
    /// </summary>
    public MessageSecurityBuilder WithAesGcmEncryption(string? keyId = null)
    {
        _services.AddAesGcmEncryption(keyId);
        return this;
    }

    /// <summary>
    /// Adds HMAC-SHA256 signing
    /// </summary>
    public MessageSecurityBuilder WithHmacSha256Signing(byte[] key, string? keyId = null)
    {
        _services.AddHmacSha256Signing(key, keyId);
        return this;
    }

    /// <summary>
    /// Adds HMAC-SHA256 signing with random key
    /// </summary>
    public MessageSecurityBuilder WithHmacSha256Signing(string? keyId = null)
    {
        _services.AddHmacSha256Signing(keyId);
        return this;
    }

    /// <summary>
    /// Adds claims-based authentication
    /// </summary>
    public MessageSecurityBuilder WithClaimsAuthentication(
        Action<ClaimsAuthenticationProvider>? configure = null,
        string scheme = "ApiKey")
    {
        if (configure != null)
        {
            _services.AddClaimsAuthentication(configure, scheme);
        }
        else
        {
            _services.AddClaimsAuthentication(scheme);
        }
        return this;
    }

    /// <summary>
    /// Adds policy-based authorization
    /// </summary>
    public MessageSecurityBuilder WithPolicyAuthorization(
        Action<PolicyAuthorizationProvider>? configure = null,
        bool requireAuthenticatedUser = true)
    {
        if (configure != null)
        {
            _services.AddPolicyAuthorization(configure, requireAuthenticatedUser);
        }
        else
        {
            _services.AddPolicyAuthorization(requireAuthenticatedUser);
        }
        return this;
    }
}
