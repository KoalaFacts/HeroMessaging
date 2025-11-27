using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using HeroMessaging.Security.Encryption;
using HeroMessaging.Security.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Security.Configuration;

/// <summary>
/// Extension methods for configuring message security
/// </summary>
public static class SecurityBuilderExtensions
{
    /// <summary>
    /// Adds AES-256-GCM encryption to the message pipeline
    /// </summary>
    public static IServiceCollection AddAesGcmEncryption(
        this IServiceCollection services,
        byte[] key,
        string? keyId = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (key == null)
            throw new ArgumentNullException(nameof(key));

        services.AddSingleton<IMessageEncryptor>(sp =>
            new AesGcmMessageEncryptor(key, keyId));

        return services;
    }

    /// <summary>
    /// Adds AES-256-GCM encryption with a randomly generated key
    /// </summary>
    public static IServiceCollection AddAesGcmEncryption(
        this IServiceCollection services,
        string? keyId = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IMessageEncryptor>(sp =>
            AesGcmMessageEncryptor.CreateWithRandomKey(keyId));

        return services;
    }

    /// <summary>
    /// Adds HMAC-SHA256 message signing
    /// </summary>
    public static IServiceCollection AddHmacSha256Signing(
        this IServiceCollection services,
        byte[] key,
        string? keyId = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (key == null)
            throw new ArgumentNullException(nameof(key));

        services.AddSingleton<IMessageSigner>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return new HmacSha256MessageSigner(key, timeProvider, keyId);
        });

        return services;
    }

    /// <summary>
    /// Adds HMAC-SHA256 signing with a randomly generated key
    /// </summary>
    public static IServiceCollection AddHmacSha256Signing(
        this IServiceCollection services,
        string? keyId = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IMessageSigner>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;
            return HmacSha256MessageSigner.CreateWithRandomKey(timeProvider, keyId);
        });

        return services;
    }

    /// <summary>
    /// Adds claims-based authentication
    /// </summary>
    public static IServiceCollection AddClaimsAuthentication(
        this IServiceCollection services,
        string scheme = "ApiKey")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IAuthenticationProvider>(sp =>
            new ClaimsAuthenticationProvider(scheme));

        return services;
    }

    /// <summary>
    /// Adds claims-based authentication with a configuration action
    /// </summary>
    public static IServiceCollection AddClaimsAuthentication(
        this IServiceCollection services,
        Action<ClaimsAuthenticationProvider> configure,
        string scheme = "ApiKey")
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var provider = new ClaimsAuthenticationProvider(scheme);
        configure(provider);

        services.AddSingleton<IAuthenticationProvider>(provider);

        return services;
    }

    /// <summary>
    /// Adds policy-based authorization
    /// </summary>
    public static IServiceCollection AddPolicyAuthorization(
        this IServiceCollection services,
        bool requireAuthenticatedUser = true)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IAuthorizationProvider>(sp =>
            new PolicyAuthorizationProvider(requireAuthenticatedUser));

        return services;
    }

    /// <summary>
    /// Adds policy-based authorization with a configuration action
    /// </summary>
    public static IServiceCollection AddPolicyAuthorization(
        this IServiceCollection services,
        Action<PolicyAuthorizationProvider> configure,
        bool requireAuthenticatedUser = true)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser);
        configure(provider);

        services.AddSingleton<IAuthorizationProvider>(provider);

        return services;
    }

    /// <summary>
    /// Adds complete message security with encryption, signing, authentication, and authorization
    /// </summary>
    public static IServiceCollection AddMessageSecurity(
        this IServiceCollection services,
        Action<MessageSecurityBuilder>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var builder = new MessageSecurityBuilder(services);
        configure?.Invoke(builder);

        return services;
    }
}

/// <summary>
/// Builder for configuring message security
/// </summary>
public sealed class MessageSecurityBuilder
{
    private readonly IServiceCollection _services;

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
