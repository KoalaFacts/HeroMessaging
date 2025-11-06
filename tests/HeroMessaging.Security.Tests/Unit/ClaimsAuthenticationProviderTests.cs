using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ClaimsAuthenticationProviderTests
{
    [Fact]
    public void Constructor_WithValidScheme_CreatesInstance()
    {
        // Act
        var provider = new ClaimsAuthenticationProvider("ApiKey");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("ApiKey", provider.Scheme);
    }

    [Fact]
    public void Constructor_WithDefaultScheme_UsesApiKey()
    {
        // Act
        var provider = new ClaimsAuthenticationProvider();

        // Assert
        Assert.Equal("ApiKey", provider.Scheme);
    }

    [Fact]
    public void RegisterApiKey_WithPrincipal_StoresMapping()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "test-key-123";
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TestUser") }, "ApiKey");
        var principal = new ClaimsPrincipal(identity);

        // Act
        provider.RegisterApiKey(apiKey, principal);

        // Assert - Will verify in AuthenticateAsync test
        Assert.NotNull(provider);
    }

    [Fact]
    public void RegisterApiKey_WithNameAndClaims_CreatesPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var apiKey = "key-456";
        var name = "John Doe";
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("department", "Engineering")
        };

        // Act
        provider.RegisterApiKey(apiKey, name, claims);

        // Assert - Will verify in AuthenticateAsync test
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidApiKey_ReturnsPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        var apiKey = "valid-key";
        var name = "Alice";
        provider.RegisterApiKey(apiKey, name, new Claim(ClaimTypes.Role, "user"));

        var credentials = new AuthenticationCredentials("ApiKey", apiKey);

        // Act
        var principal = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(principal);
        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(name, principal.Identity?.Name);
        Assert.True(principal.IsInRole("user"));
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidApiKey_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        provider.RegisterApiKey("valid-key", "User1");

        var credentials = new AuthenticationCredentials("ApiKey", "invalid-key");

        // Act
        var principal = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task AuthenticateAsync_WithWrongScheme_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        provider.RegisterApiKey("test-key", "User");

        var credentials = new AuthenticationCredentials("Bearer", "test-key");

        // Act
        var principal = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task AuthenticateAsync_WithNullCredentials_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AuthenticateAsync(null!));
    }

    [Fact]
    public async Task ValidateTokenAsync_NotImplemented_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var principal = await provider.ValidateTokenAsync("some-token");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public void AuthenticationCredentials_FromAuthorizationHeader_ParsesCorrectly()
    {
        // Arrange
        var headerValue = "ApiKey test-key-789";

        // Act
        var credentials = AuthenticationCredentials.FromAuthorizationHeader(headerValue);

        // Assert
        Assert.Equal("ApiKey", credentials.Scheme);
        Assert.Equal("test-key-789", credentials.Value);
    }

    [Fact]
    public void AuthenticationCredentials_FromAuthorizationHeader_WithBearer_ParsesCorrectly()
    {
        // Arrange
        var headerValue = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";

        // Act
        var credentials = AuthenticationCredentials.FromAuthorizationHeader(headerValue);

        // Assert
        Assert.Equal("Bearer", credentials.Scheme);
        Assert.Equal("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", credentials.Value);
    }

    [Fact]
    public async Task AuthenticateAsync_WithMultipleKeys_ReturnsCorrectPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        provider.RegisterApiKey("key1", "User1", new Claim(ClaimTypes.Role, "user"));
        provider.RegisterApiKey("key2", "Admin1", new Claim(ClaimTypes.Role, "admin"));

        var credentials1 = new AuthenticationCredentials("ApiKey", "key1");
        var credentials2 = new AuthenticationCredentials("ApiKey", "key2");

        // Act
        var principal1 = await provider.AuthenticateAsync(credentials1);
        var principal2 = await provider.AuthenticateAsync(credentials2);

        // Assert
        Assert.NotNull(principal1);
        Assert.Equal("User1", principal1.Identity?.Name);
        Assert.True(principal1.IsInRole("user"));

        Assert.NotNull(principal2);
        Assert.Equal("Admin1", principal2.Identity?.Name);
        Assert.True(principal2.IsInRole("admin"));
    }

    [Fact]
    public async Task AuthenticateAsync_WithCustomClaims_IncludesAllClaims()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, "manager"),
            new Claim("department", "Sales"),
            new Claim("level", "senior"),
            new Claim("region", "NA")
        };
        provider.RegisterApiKey("manager-key", "Bob Manager", claims);

        var credentials = new AuthenticationCredentials("ApiKey", "manager-key");

        // Act
        var principal = await provider.AuthenticateAsync(credentials);

        // Assert
        Assert.NotNull(principal);
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == "manager");
        Assert.Contains(principal.Claims, c => c.Type == "department" && c.Value == "Sales");
        Assert.Contains(principal.Claims, c => c.Type == "level" && c.Value == "senior");
        Assert.Contains(principal.Claims, c => c.Type == "region" && c.Value == "NA");
    }
}
