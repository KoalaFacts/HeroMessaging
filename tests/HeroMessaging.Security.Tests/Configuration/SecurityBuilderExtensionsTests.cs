using System.Security.Cryptography;
using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Encryption;
using HeroMessaging.Security.Signing;
using HeroMessaging.Security.Authentication;
using HeroMessaging.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Security.Tests.Configuration;

/// <summary>
/// Unit tests for SecurityBuilderExtensions
/// </summary>
public sealed class SecurityBuilderExtensionsTests
{
    private static byte[] GenerateRandomKey(int size = 32)
    {
        var key = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return key;
    }

    #region AddAesGcmEncryption Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_WithKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        var result = services.AddAesGcmEncryption(key);

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.IsType<AesGcmMessageEncryptor>(encryptor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_WithKeyAndKeyId_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();
        var keyId = "key-1";

        // Act
        var result = services.AddAesGcmEncryption(key, keyId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(services, d => d.ServiceType == typeof(IMessageEncryptor));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var key = GenerateRandomKey();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddAesGcmEncryption(key));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - explicitly cast to byte[] to call correct overload
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddAesGcmEncryption((byte[])null!));
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_RandomKey_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddAesGcmEncryption();

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
        Assert.IsType<AesGcmMessageEncryptor>(encryptor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_RandomKeyWithKeyId_RegistersEncryptor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddAesGcmEncryption("random-key");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddAesGcmEncryption_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        var result = services.AddAesGcmEncryption(key);

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region AddHmacSha256Signing Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_WithKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        var result = services.AddHmacSha256Signing(key);

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var signer = provider.GetRequiredService<IMessageSigner>();
        Assert.NotNull(signer);
        Assert.IsType<HmacSha256MessageSigner>(signer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_WithKeyAndKeyId_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();
        var keyId = "key-1";

        // Act
        var result = services.AddHmacSha256Signing(key, keyId);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var key = GenerateRandomKey();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddHmacSha256Signing(key));
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - explicitly cast to byte[] to call correct overload
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddHmacSha256Signing((byte[])null!));
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_RandomKey_RegistersSigner()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddHmacSha256Signing();

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var signer = provider.GetRequiredService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddHmacSha256Signing_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        var result = services.AddHmacSha256Signing(key);

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region AddClaimsAuthentication Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithDefaultScheme_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddClaimsAuthentication();

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
        Assert.IsType<ClaimsAuthenticationProvider>(authProvider);
        Assert.Equal("ApiKey", authProvider.Scheme);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithCustomScheme_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var scheme = "Bearer";

        // Act
        var result = services.AddClaimsAuthentication(scheme);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithConfiguration_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<ClaimsAuthenticationProvider> configure = p =>
        {
            p.RegisterApiKey("test-key", "TestUser", Array.Empty<Claim>());
        };

        // Act
        var result = services.AddClaimsAuthentication(configure);

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithConfigurationAndScheme_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<ClaimsAuthenticationProvider> configure = p =>
        {
            p.RegisterApiKey("key1", "User1", Array.Empty<Claim>());
        };
        var scheme = "CustomScheme";

        // Act
        var result = services.AddClaimsAuthentication(configure, scheme);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddClaimsAuthentication());
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<ClaimsAuthenticationProvider> configure = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddClaimsAuthentication(configure));
        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddClaimsAuthentication_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddClaimsAuthentication();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region AddPolicyAuthorization Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithDefaults_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddPolicyAuthorization();

        // Assert
        Assert.NotNull(result);
        var provider = services.BuildServiceProvider();
        var authzProvider = provider.GetRequiredService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
        Assert.IsType<PolicyAuthorizationProvider>(authzProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithRequireAuthenticatedUserFalse_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddPolicyAuthorization(requireAuthenticatedUser: false);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithConfiguration_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<PolicyAuthorizationProvider> configure = p =>
        {
            p.AllowAnonymous("PublicMessage", "Receive");
        };

        // Act
        var result = services.AddPolicyAuthorization(configure);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithConfigurationAndSettings_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<PolicyAuthorizationProvider> configure = p =>
        {
            p.RequireRole("AdminMessage", "Send", "Admin");
        };

        // Act
        var result = services.AddPolicyAuthorization(configure, requireAuthenticatedUser: true);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddPolicyAuthorization());
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<PolicyAuthorizationProvider> configure = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddPolicyAuthorization(configure));
        Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicyAuthorization_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddPolicyAuthorization();

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region MessageSecurityBuilder Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithAesGcmEncryption_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithAesGcmEncryption(key);
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithAesGcmEncryptionRandomKey_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithAesGcmEncryption();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        Assert.NotNull(encryptor);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithHmacSha256Signing_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var key = GenerateRandomKey();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithHmacSha256Signing(key);
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var signer = provider.GetRequiredService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithHmacSha256SigningRandomKey_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithHmacSha256Signing();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var signer = provider.GetRequiredService<IMessageSigner>();
        Assert.NotNull(signer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithClaimsAuthentication_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithClaimsAuthentication();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithClaimsAuthenticationConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithClaimsAuthentication(auth =>
            {
                auth.RegisterApiKey("key1", "User1", Array.Empty<Claim>());
            }, "ApiKey");
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        Assert.NotNull(authProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithPolicyAuthorization_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithPolicyAuthorization();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var authzProvider = provider.GetRequiredService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_WithPolicyAuthorizationConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder.WithPolicyAuthorization(policy =>
            {
                policy.AllowAnonymous("PublicMessage", "Receive");
            });
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var authzProvider = provider.GetRequiredService<IAuthorizationProvider>();
        Assert.NotNull(authzProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MessageSecurityBuilder_ChainedConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var encryptionKey = GenerateRandomKey();
        var signingKey = GenerateRandomKey();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder
                .WithAesGcmEncryption(encryptionKey, "enc-key-1")
                .WithHmacSha256Signing(signingKey, "sig-key-1")
                .WithClaimsAuthentication(null, "Bearer")
                .WithPolicyAuthorization();
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        var signer = provider.GetRequiredService<IMessageSigner>();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        var authzProvider = provider.GetRequiredService<IAuthorizationProvider>();

        Assert.NotNull(encryptor);
        Assert.NotNull(signer);
        Assert.NotNull(authProvider);
        Assert.NotNull(authzProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddMessageSecurity_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => services.AddMessageSecurity());
        Assert.Equal("services", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddMessageSecurity_WithoutConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddMessageSecurity();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddMessageSecurity_WithNullConfiguration_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddMessageSecurity(null);

        // Assert
        Assert.Same(services, result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void CompleteSecuritySetup_WithAllComponents_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddMessageSecurity(builder =>
        {
            builder
                .WithAesGcmEncryption()
                .WithHmacSha256Signing()
                .WithClaimsAuthentication(auth =>
                {
                    auth.RegisterApiKey("admin-key", "Administrator", new[]
                    {
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim("permission", "write")
                    });
                    auth.RegisterApiKey("user-key", "RegularUser", new[]
                    {
                        new Claim(ClaimTypes.Role, "User"),
                        new Claim("permission", "read")
                    });
                })
                .WithPolicyAuthorization(policy =>
                {
                    policy.RequireRole("AdminMessage", "Send", "Admin");
                    policy.AllowAnonymous("PublicMessage", "Receive");
                });
        });

        // Act & Assert
        var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IMessageEncryptor>();
        var signer = provider.GetRequiredService<IMessageSigner>();
        var authProvider = provider.GetRequiredService<IAuthenticationProvider>();
        var authzProvider = provider.GetRequiredService<IAuthorizationProvider>();

        Assert.NotNull(encryptor);
        Assert.NotNull(signer);
        Assert.NotNull(authProvider);
        Assert.NotNull(authzProvider);
    }

    #endregion
}
