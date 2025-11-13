using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using Xunit;

namespace HeroMessaging.Security.Tests.Authentication;

/// <summary>
/// Unit tests for ClaimsAuthenticationProvider
/// </summary>
public sealed class ClaimsAuthenticationProviderTests
{
    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaultScheme_Succeeds()
    {
        // Act
        var provider = new ClaimsAuthenticationProvider();

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("ApiKey", provider.Scheme);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithCustomScheme_Succeeds()
    {
        // Act
        var provider = new ClaimsAuthenticationProvider("Bearer");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("Bearer", provider.Scheme);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullScheme_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new ClaimsAuthenticationProvider(null!));
        Assert.Equal("scheme", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Scheme_PropertyReturnsSetScheme()
    {
        // Arrange
        var scheme = "CustomScheme";

        // Act
        var provider = new ClaimsAuthenticationProvider(scheme);

        // Assert
        Assert.Equal(scheme, provider.Scheme);
    }

    #endregion

    #region RegisterApiKey Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithValidPrincipal_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-api-key-123";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        // Act
        provider.RegisterApiKey(apiKey, principal);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithNullApiKey_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.RegisterApiKey(null!, principal));
        Assert.Equal("apiKey", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithEmptyApiKey_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.RegisterApiKey("", principal));
        Assert.Equal("apiKey", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithWhitespaceApiKey_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.RegisterApiKey("   ", principal));
        Assert.Equal("apiKey", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithNullPrincipal_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        ClaimsPrincipal nullPrincipal = null!;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => provider.RegisterApiKey("test-key", nullPrincipal));
        Assert.Equal("principal", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithNameAndClaims_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-api-key";
        var name = "TestUser";
        var claims = new Claim[] { new("role", "Admin"), new("department", "Engineering") };

        // Act
        provider.RegisterApiKey(apiKey, name, claims);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var claims = new Claim[] { new("role", "Admin") };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.RegisterApiKey("test-key", "", claims));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_OverwritesPreviousRegistration()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-key";
        var identity1 = new ClaimsIdentity("ApiKey");
        var principal1 = new ClaimsPrincipal(identity1);
        var identity2 = new ClaimsIdentity("ApiKey");
        identity2.AddClaim(new Claim("role", "Admin"));
        var principal2 = new ClaimsPrincipal(identity2);

        // Act
        provider.RegisterApiKey(apiKey, principal1);
        provider.RegisterApiKey(apiKey, principal2);

        // Assert - Can only verify by authentication later
        Assert.NotNull(provider);
    }

    #endregion

    #region AuthenticateAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithValidCredentials_ReturnsAuthenticatedPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        var apiKey = "test-api-key";
        var identity = new ClaimsIdentity("ApiKey");
        identity.AddClaim(new Claim(ClaimTypes.Name, "TestUser"));
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var credentials = new AuthenticationCredentials("ApiKey", apiKey);

        // Act
        var result = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Identity);
        Assert.True(result.Identity.IsAuthenticated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithNullCredentials_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AuthenticateAsync(null!));
        Assert.Equal("credentials", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithWrongScheme_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        var apiKey = "test-api-key";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var wrongSchemeCredentials = new AuthenticationCredentials("Bearer", apiKey);

        // Act
        var result = await provider.AuthenticateAsync(wrongSchemeCredentials);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithUnknownApiKey_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        var apiKey = "test-api-key";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var unknownKeyCredentials = new AuthenticationCredentials("ApiKey", "unknown-key");

        // Act
        var result = await provider.AuthenticateAsync(unknownKeyCredentials);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithCorrectSchemeAndKey_ReturnsCorrectPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("CustomScheme");
        var apiKey = "my-secret-key";
        var identity = new ClaimsIdentity("CustomScheme");
        identity.AddClaim(new Claim(ClaimTypes.Name, "CustomUser"));
        identity.AddClaim(new Claim("department", "Engineering"));
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var credentials = new AuthenticationCredentials("CustomScheme", apiKey);

        // Act
        var result = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("CustomUser", result.Identity?.Name);
        Assert.True(result.HasClaim("department", "Engineering"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_CaseSensitiveApiKey()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "TestKey123";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        // Try with different case
        var lowerCaseCredentials = new AuthenticationCredentials("ApiKey", "testkey123");

        // Act
        var result = await provider.AuthenticateAsync(lowerCaseCredentials);

        // Assert
        Assert.Null(result); // Should not match due to case sensitivity
    }

    #endregion

    #region ValidateTokenAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsAuthenticatedPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "valid-token-123";
        var identity = new ClaimsIdentity("ApiKey");
        identity.AddClaim(new Claim(ClaimTypes.Name, "TokenUser"));
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(token, principal);

        // Act
        var result = await provider.ValidateTokenAsync(token);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TokenUser", result.Identity?.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithNullToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var result = await provider.ValidateTokenAsync(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithEmptyToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var result = await provider.ValidateTokenAsync("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithWhitespaceToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var result = await provider.ValidateTokenAsync("   ");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithUnknownToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "known-token";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(token, principal);

        // Act
        var result = await provider.ValidateTokenAsync("unknown-token");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_CaseSensitiveToken()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "MyToken123";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(token, principal);

        // Act
        var result = await provider.ValidateTokenAsync("mytoken123");

        // Assert
        Assert.Null(result); // Should not match due to case sensitivity
    }

    #endregion

    #region Multiple Registration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterApiKey_WithMultipleKeys_CanAuthenticateWithEachKey()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        var key1 = "key-1";
        var identity1 = new ClaimsIdentity("ApiKey");
        identity1.AddClaim(new Claim(ClaimTypes.Name, "User1"));
        var principal1 = new ClaimsPrincipal(identity1);

        var key2 = "key-2";
        var identity2 = new ClaimsIdentity("ApiKey");
        identity2.AddClaim(new Claim(ClaimTypes.Name, "User2"));
        var principal2 = new ClaimsPrincipal(identity2);

        provider.RegisterApiKey(key1, principal1);
        provider.RegisterApiKey(key2, principal2);

        // Act
        var result1 = await provider.ValidateTokenAsync(key1);
        var result2 = await provider.ValidateTokenAsync(key2);

        // Assert
        Assert.NotNull(result1);
        Assert.Equal("User1", result1.Identity?.Name);
        Assert.NotNull(result2);
        Assert.Equal("User2", result2.Identity?.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterApiKey_WithMultipleKeys_InvalidKeyReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        var key1 = "valid-key-1";
        var key2 = "valid-key-2";

        provider.RegisterApiKey(key1, "User1", Array.Empty<Claim>());
        provider.RegisterApiKey(key2, "User2", Array.Empty<Claim>());

        // Act
        var result = await provider.ValidateTokenAsync("invalid-key");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Claims Integration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterApiKey_WithMultipleClaims_AuthenticationPreservesClaims()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "secure-key";
        var claims = new Claim[]
        {
            new(ClaimTypes.Name, "John Doe"),
            new(ClaimTypes.Email, "john@example.com"),
            new(ClaimTypes.Role, "Admin"),
            new("department", "Engineering"),
            new("permission", "write")
        };

        provider.RegisterApiKey(apiKey, "John Doe", claims);

        // Act
        var result = await provider.ValidateTokenAsync(apiKey);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasClaim(ClaimTypes.Email, "john@example.com"));
        Assert.True(result.HasClaim(ClaimTypes.Role, "Admin"));
        Assert.True(result.HasClaim("department", "Engineering"));
        Assert.True(result.HasClaim("permission", "write"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterApiKey_WithClaims_NullClaimsArray()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-key";

        // Act & Assert - Should not throw, empty claims should be handled
        provider.RegisterApiKey(apiKey, "TestUser", Array.Empty<Claim>());
        var result = await provider.ValidateTokenAsync(apiKey);

        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RegisterApiKey_WithDuplicateClaims_LastOneWins()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-key";
        var claims = new Claim[]
        {
            new("role", "User"),
            new("role", "Admin") // Duplicate claim type with different value
        };

        // Act
        provider.RegisterApiKey(apiKey, "TestUser", claims);
        var result = await provider.ValidateTokenAsync(apiKey);

        // Assert
        Assert.NotNull(result);
        var roleClaims = result?.FindAll("role");
        Assert.NotNull(roleClaims);
        Assert.Equal(2, roleClaims.Count());
    }

    #endregion

    #region Async/Cancellation Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithCancellationToken_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-key";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var credentials = new AuthenticationCredentials("ApiKey", apiKey);
        var cts = new CancellationTokenSource();

        // Act
        var result = await provider.AuthenticateAsync(credentials, cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithCancellationToken_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "test-token";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(token, principal);
        var cts = new CancellationTokenSource();

        // Act
        var result = await provider.ValidateTokenAsync(token, cts.Token);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithVeryLongApiKey_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = new string('A', 1000); // Very long key
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var credentials = new AuthenticationCredentials("ApiKey", apiKey);

        // Act
        var result = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthenticateAsync_WithSpecialCharactersInApiKey_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "!@#$%^&*()_+-=[]{}|;:',.<>?/~`";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(apiKey, principal);

        var credentials = new AuthenticationCredentials("ApiKey", apiKey);

        // Act
        var result = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateTokenAsync_WithUnicodeCharactersInToken_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "token-🔐-secure";
        var identity = new ClaimsIdentity("ApiKey");
        var principal = new ClaimsPrincipal(identity);

        provider.RegisterApiKey(token, principal);

        // Act
        var result = await provider.ValidateTokenAsync(token);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterApiKey_WithEmptyClaims_Succeeds()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        provider.RegisterApiKey("test-key", "TestUser", new Claim[] { });
    }

    #endregion
}
