using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using HeroMessaging.Security.Encryption;
using HeroMessaging.Security.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring message security
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIServiceCollectionForSecurity
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
        Action<HeroMessaging.Security.MessageSecurityBuilder>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var builder = new HeroMessaging.Security.MessageSecurityBuilder(services);
        configure?.Invoke(builder);

        return services;
    }
}
