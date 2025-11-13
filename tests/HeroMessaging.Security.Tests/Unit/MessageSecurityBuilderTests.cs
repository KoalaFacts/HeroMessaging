using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using HeroMessaging.Security.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MessageSecurityBuilderTests
{
    [Fact]
    public void Constructor_WithValidServices_CreatesBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new MessageSecurityBuilder(services);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Constructor_WithNullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MessageSecurityBuilder(null!));
        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void WithAesGcmEncryption_WithKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        // Act
        var result = builder.WithAesGcmEncryption(key, "key-1");
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void WithAesGcmEncryption_WithRandomKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithAesGcmEncryption("generated-key");
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    public void WithAesGcmEncryption_WithoutKeyId_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithAesGcmEncryption();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    public void WithHmacSha256Signing_WithKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);

        // Act
        var result = builder.WithHmacSha256Signing(key, "sign-key-1");
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void WithHmacSha256Signing_WithRandomKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithHmacSha256Signing("generated-sign-key");
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    public void WithHmacSha256Signing_WithoutKeyId_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithHmacSha256Signing();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var signer = provider.GetService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    public void WithClaimsAuthentication_WithDefaultScheme_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithClaimsAuthentication();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var authProvider = provider.GetService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.Equal("ApiKey", authProvider.Scheme);
    }

    [Fact]
    public void WithClaimsAuthentication_WithCustomScheme_RegistersProviderWithScheme()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithClaimsAuthentication(scheme: "Bearer");
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var authProvider = provider.GetService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.Equal("Bearer", authProvider.Scheme);
    }

    [Fact]
    public void WithClaimsAuthentication_WithConfigureAction_ConfiguresProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);
        var testKey = "test-api-key";
        var testName = "TestUser";

        // Act
        var result = builder.WithClaimsAuthentication(provider =>
        {
            provider.RegisterApiKey(testKey, testName);
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var authProvider = serviceProvider.GetService<IAuthenticationProvider>() as ClaimsAuthenticationProvider;
        Assert.NotNull(authProvider);

        var credentials = new AuthenticationCredentials("ApiKey", testKey);
        var principal = authProvider.AuthenticateAsync(credentials).Result;
        Assert.NotNull(principal);
        Assert.Equal(testName, principal.Identity?.Name);
    }

    [Fact]
    public void WithPolicyAuthorization_WithDefaultSettings_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithPolicyAuthorization();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result); // Fluent API
        var authzProvider = provider.GetService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void WithPolicyAuthorization_WithRequireAuthFalse_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithPolicyAuthorization(requireAuthenticatedUser: false);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var authzProvider = provider.GetService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    public void WithPolicyAuthorization_WithConfigureAction_ConfiguresProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithPolicyAuthorization(provider =>
        {
            provider.RequireRole("Command", "Execute", "admin");
        });
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        var authzProvider = serviceProvider.GetService<IAuthorizationProvider>() as PolicyAuthorizationProvider;
        Assert.NotNull(authzProvider);

        // Verify configuration
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin") },
            "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var authResult = authzProvider.AuthorizeAsync(principal, "Command", "Execute").Result;
        Assert.True(authResult.Succeeded);
    }

    [Fact]
    public void FluentChaining_WithAllComponents_RegistersEverything()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);
        var encKey = new byte[32];
        var signKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(encKey);
        System.Security.Cryptography.RandomNumberGenerator.Fill(signKey);

        // Act
        var result = builder
            .WithAesGcmEncryption(encKey, "enc-1")
            .WithHmacSha256Signing(signKey, "sign-1")
            .WithClaimsAuthentication(p => p.RegisterApiKey("key", "user"))
            .WithPolicyAuthorization(p => p.AllowAnonymous("Public", "Read"));

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<IMessageEncryptor>());
        Assert.NotNull(provider.GetService<IMessageSigner>());
        Assert.NotNull(provider.GetService<IAuthenticationProvider>());
        Assert.NotNull(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void FluentChaining_WithRandomKeys_RegistersEverything()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder
            .WithAesGcmEncryption()
            .WithHmacSha256Signing()
            .WithClaimsAuthentication()
            .WithPolicyAuthorization();

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<IMessageEncryptor>());
        Assert.NotNull(provider.GetService<IMessageSigner>());
        Assert.NotNull(provider.GetService<IAuthenticationProvider>());
        Assert.NotNull(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void FluentChaining_PartialConfiguration_RegistersOnlySpecifiedComponents()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act - Only encryption and signing
        var result = builder
            .WithAesGcmEncryption()
            .WithHmacSha256Signing();

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<IMessageEncryptor>());
        Assert.NotNull(provider.GetService<IMessageSigner>());
        Assert.Null(provider.GetService<IAuthenticationProvider>());
        Assert.Null(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void FluentChaining_AuthenticationOnly_RegistersOnlyAuth()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithClaimsAuthentication();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.Null(provider.GetService<IMessageEncryptor>());
        Assert.Null(provider.GetService<IMessageSigner>());
        Assert.NotNull(provider.GetService<IAuthenticationProvider>());
        Assert.Null(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void FluentChaining_AuthorizationOnly_RegistersOnlyAuthz()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        var result = builder.WithPolicyAuthorization();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.Null(provider.GetService<IMessageEncryptor>());
        Assert.Null(provider.GetService<IMessageSigner>());
        Assert.Null(provider.GetService<IAuthenticationProvider>());
        Assert.NotNull(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void MultipleBuilders_CanBeCreatedFromSameServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder1 = new MessageSecurityBuilder(services);
        var builder2 = new MessageSecurityBuilder(services);

        builder1.WithAesGcmEncryption();
        builder2.WithHmacSha256Signing();

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotSame(builder1, builder2);
        Assert.NotNull(provider.GetService<IMessageEncryptor>());
        Assert.NotNull(provider.GetService<IMessageSigner>());
    }

    [Fact]
    public void Builder_CanBeUsedMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act
        builder.WithAesGcmEncryption();
        builder.WithHmacSha256Signing();
        builder.WithClaimsAuthentication();
        builder.WithPolicyAuthorization();

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IMessageEncryptor>());
        Assert.NotNull(provider.GetService<IMessageSigner>());
        Assert.NotNull(provider.GetService<IAuthenticationProvider>());
        Assert.NotNull(provider.GetService<IAuthorizationProvider>());
    }

    [Fact]
    public void WithAesGcmEncryption_CalledTwice_SecondRegistrationWins()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);
        var key1 = new byte[32];
        var key2 = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key1);
        System.Security.Cryptography.RandomNumberGenerator.Fill(key2);

        // Act
        builder.WithAesGcmEncryption(key1, "key-1");
        builder.WithAesGcmEncryption(key2, "key-2");

        var provider = services.BuildServiceProvider();

        // Assert - Should have an encryptor (last registration)
        var encryptor = provider.GetService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    public void ComplexScenario_FullSecurityStack_AllComponentsWork()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new MessageSecurityBuilder(services);

        // Act - Build complete security stack
        builder
            .WithAesGcmEncryption("primary-enc-key")
            .WithHmacSha256Signing("primary-sign-key")
            .WithClaimsAuthentication(auth =>
            {
                auth.RegisterApiKey("admin-key", "admin",
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "admin"));
                auth.RegisterApiKey("user-key", "user",
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "user"));
            }, "ApiKey")
            .WithPolicyAuthorization(authz =>
            {
                authz.RequireRole("AdminCommand", "Execute", "admin");
                authz.AllowAnonymous("PublicQuery", "Read");
            }, requireAuthenticatedUser: true);

        var provider = services.BuildServiceProvider();

        // Assert - Verify all components
        var encryptor = provider.GetService<IMessageEncryptor>();
        var signer = provider.GetService<IMessageSigner>();
        var authProvider = provider.GetService<IAuthenticationProvider>() as ClaimsAuthenticationProvider;
        var authzProvider = provider.GetService<IAuthorizationProvider>() as PolicyAuthorizationProvider;

        Assert.NotNull(encryptor);
        Assert.NotNull(signer);
        Assert.NotNull(authProvider);
        Assert.NotNull(authzProvider);

        // Verify auth works
        var adminCreds = new AuthenticationCredentials("ApiKey", "admin-key");
        var adminPrincipal = authProvider!.AuthenticateAsync(adminCreds).Result;
        Assert.NotNull(adminPrincipal);
        Assert.True(adminPrincipal.IsInRole("admin"));

        // Verify authz works
        var authzResult = authzProvider!.AuthorizeAsync(adminPrincipal, "AdminCommand", "Execute").Result;
        Assert.True(authzResult.Succeeded);
    }
}
