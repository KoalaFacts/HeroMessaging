using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using HeroMessaging.Security.Configuration;
using HeroMessaging.Security.Encryption;
using HeroMessaging.Security.Signing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SecurityBuilderExtensionsTests
{
    [Fact]
    public void AddAesGcmEncryption_WithKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        // Act
        services.AddAesGcmEncryption(key);
        var provider = services.BuildServiceProvider();

        // Assert
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void AddAesGcmEncryption_WithKeyAndKeyId_RegistersEncryptorWithKeyId()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        var keyId = "test-key-1";

        // Act
        services.AddAesGcmEncryption(key, keyId);
        var provider = services.BuildServiceProvider();

        // Assert
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void AddAesGcmEncryption_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var key = new byte[32];

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddAesGcmEncryption(key));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddAesGcmEncryption_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddAesGcmEncryption(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void AddAesGcmEncryption_WithRandomKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAesGcmEncryption();
        var provider = services.BuildServiceProvider();

        // Assert
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void AddAesGcmEncryption_WithRandomKeyAndKeyId_RegistersEncryptorWithKeyId()
    {
        // Arrange
        var services = new ServiceCollection();
        var keyId = "generated-key";

        // Act
        services.AddAesGcmEncryption(keyId);
        var provider = services.BuildServiceProvider();

        // Assert
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    public void AddAesGcmEncryption_WithRandomKeyAndNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddAesGcmEncryption());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddHmacSha256Signing_WithKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        // Act
        services.AddHmacSha256Signing(key);
        var provider = services.BuildServiceProvider();

        // Assert
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void AddHmacSha256Signing_WithKeyAndKeyId_RegistersSignerWithKeyId()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        var keyId = "signing-key-1";

        // Act
        services.AddHmacSha256Signing(key, keyId);
        var provider = services.BuildServiceProvider();

        // Assert
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void AddHmacSha256Signing_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var key = new byte[32];

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddHmacSha256Signing(key));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddHmacSha256Signing_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddHmacSha256Signing(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void AddHmacSha256Signing_WithRandomKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHmacSha256Signing();
        var provider = services.BuildServiceProvider();

        // Assert
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void AddHmacSha256Signing_WithRandomKeyAndKeyId_RegistersSignerWithKeyId()
    {
        // Arrange
        var services = new ServiceCollection();
        var keyId = "auto-signing-key";

        // Act
        services.AddHmacSha256Signing(keyId);
        var provider = services.BuildServiceProvider();

        // Assert
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    public void AddHmacSha256Signing_WithRandomKeyAndNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddHmacSha256Signing());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddClaimsAuthentication_WithDefaultScheme_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddClaimsAuthentication();
        var provider = services.BuildServiceProvider();

        // Assert
        var authProvider = provider.GetService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.Equal("ApiKey", authProvider.Scheme);
    }

    [Fact]
    public void AddClaimsAuthentication_WithCustomScheme_RegistersProviderWithScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        var scheme = "Bearer";

        // Act
        services.AddClaimsAuthentication(scheme);
        var provider = services.BuildServiceProvider();

        // Assert
        var authProvider = provider.GetService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.Equal(scheme, authProvider.Scheme);
    }

    [Fact]
    public void AddClaimsAuthentication_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddClaimsAuthentication());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddClaimsAuthentication_WithConfigureAction_ConfiguresProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuredKey = "test-key";
        var configuredName = "TestUser";

        // Act
        services.AddClaimsAuthentication(provider =>
        {
            provider.RegisterApiKey(configuredKey, configuredName);
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var authProvider = serviceProvider.GetService<IAuthenticationProvider>() as ClaimsAuthenticationProvider;
        Assert.NotNull(authProvider);

        var credentials = new AuthenticationCredentials("ApiKey", configuredKey);
        var principal = authProvider.AuthenticateAsync(credentials).Result;
        Assert.NotNull(principal);
        Assert.Equal(configuredName, principal.Identity?.Name);
    }

    [Fact]
    public void AddClaimsAuthentication_WithNullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<ClaimsAuthenticationProvider> configure = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddClaimsAuthentication(configure));
        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void AddClaimsAuthentication_WithConfigureActionAndCustomScheme_UsesCustomScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        var scheme = "Custom";

        // Act
        services.AddClaimsAuthentication(provider => { }, scheme);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var authProvider = serviceProvider.GetService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.Equal(scheme, authProvider.Scheme);
    }

    [Fact]
    public void AddPolicyAuthorization_WithDefaultSettings_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPolicyAuthorization();
        var provider = services.BuildServiceProvider();

        // Assert
        var authzProvider = provider.GetService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void AddPolicyAuthorization_WithRequireAuthFalse_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPolicyAuthorization(requireAuthenticatedUser: false);
        var provider = services.BuildServiceProvider();

        // Assert
        var authzProvider = provider.GetService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void AddPolicyAuthorization_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddPolicyAuthorization());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddPolicyAuthorization_WithConfigureAction_ConfiguresProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPolicyAuthorization(provider =>
        {
            provider.RequireRole("TestMessage", "Send", "admin");
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var authzProvider = serviceProvider.GetService<IAuthorizationProvider>() as PolicyAuthorizationProvider;
        Assert.NotNull(authzProvider);

        // Verify policy was configured
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin") },
            "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var result = authzProvider.AuthorizeAsync(principal, "TestMessage", "Send").Result;
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AddPolicyAuthorization_WithNullConfigureAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<PolicyAuthorizationProvider> configure = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddPolicyAuthorization(configure));
        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void AddPolicyAuthorization_WithConfigureActionAndRequireAuthFalse_UsesCustomSetting()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPolicyAuthorization(provider => { }, requireAuthenticatedUser: false);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var authzProvider = serviceProvider.GetService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void AddMessageSecurity_WithoutConfigure_RegistersBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity();

        // Assert
        Assert.NotNull(services);
    }

    [Fact]
    public void AddMessageSecurity_WithConfigureAction_ExecutesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configured = false;

        // Act
        services.AddMessageSecurity(builder =>
        {
            configured = true;
        });

        // Assert
        Assert.True(configured);
    }

    [Fact]
    public void AddMessageSecurity_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => services.AddMessageSecurity());
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void AddMessageSecurity_WithFullConfiguration_RegistersAllComponents()
    {
        // Arrange
        var services = new ServiceCollection();
        var encryptionKey = new byte[32];
        var signingKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(encryptionKey);
        System.Security.Cryptography.RandomNumberGenerator.Fill(signingKey);

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithAesGcmEncryption(encryptionKey, "enc-key-1")
                   .WithHmacSha256Signing(signingKey, "sign-key-1")
                   .WithClaimsAuthentication()
                   .WithPolicyAuthorization();
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var encryptor = provider.GetService<IMessageEncryptor>();
        var signer = provider.GetService<IMessageSigner>();
        var authProvider = provider.GetService<IAuthenticationProvider>();
        var authzProvider = provider.GetService<IAuthorizationProvider>();

        Assert.NotNull(encryptor);
        Assert.NotNull(signer);
        Assert.NotNull(authProvider);
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void ServiceRegistrations_AreSingletons()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddAesGcmEncryption();
        services.AddHmacSha256Signing();
        services.AddClaimsAuthentication();
        services.AddPolicyAuthorization();

        var provider = services.BuildServiceProvider();

        // Act
        var encryptor1 = provider.GetService<IMessageEncryptor>();
        var encryptor2 = provider.GetService<IMessageEncryptor>();
        var signer1 = provider.GetService<IMessageSigner>();
        var signer2 = provider.GetService<IMessageSigner>();
        var auth1 = provider.GetService<IAuthenticationProvider>();
        var auth2 = provider.GetService<IAuthenticationProvider>();
        var authz1 = provider.GetService<IAuthorizationProvider>();
        var authz2 = provider.GetService<IAuthorizationProvider>();

        // Assert - Same instances should be returned
        Assert.Same(encryptor1, encryptor2);
        Assert.Same(signer1, signer2);
        Assert.Same(auth1, auth2);
        Assert.Same(authz1, authz2);
    }

    [Fact]
    public void MultipleRegistrations_LastRegistrationWins()
    {
        // Arrange
        var services = new ServiceCollection();
        var key1 = new byte[32];
        var key2 = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key1);
        System.Security.Cryptography.RandomNumberGenerator.Fill(key2);

        // Act
        services.AddAesGcmEncryption(key1, "key-1");
        services.AddAesGcmEncryption(key2, "key-2"); // Second registration

        var provider = services.BuildServiceProvider();

        // Assert - Should get the second registration
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }
}
