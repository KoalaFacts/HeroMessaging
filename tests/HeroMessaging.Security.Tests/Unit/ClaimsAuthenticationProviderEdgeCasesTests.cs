using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authentication;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ClaimsAuthenticationProviderEdgeCasesTests
{
    [Fact]
    public void Constructor_WithNullScheme_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ClaimsAuthenticationProvider(null!));
        Assert.Equal("scheme", exception.ParamName);
    }

    [Fact]
    public void RegisterApiKey_WithEmptyString_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => provider.RegisterApiKey("", principal));
        Assert.Contains("API key cannot be empty", exception.Message);
    }

    [Fact]
    public void RegisterApiKey_WithWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => provider.RegisterApiKey("   ", principal));
        Assert.Contains("API key cannot be empty", exception.Message);
    }

    [Fact]
    public void RegisterApiKey_WithNullPrincipal_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => provider.RegisterApiKey("valid-key", (ClaimsPrincipal)null!));
        Assert.Equal("principal", exception.ParamName);
    }

    [Fact]
    public void RegisterApiKey_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => provider.RegisterApiKey("key", (string)null!));
        Assert.Contains("Name cannot be empty", exception.Message);
    }

    [Fact]
    public void RegisterApiKey_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => provider.RegisterApiKey("key", ""));
        Assert.Contains("Name cannot be empty", exception.Message);
    }

    [Fact]
    public void RegisterApiKey_WithWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => provider.RegisterApiKey("key", "  \t\n"));
        Assert.Contains("Name cannot be empty", exception.Message);
    }

    [Fact]
    public void RegisterApiKey_OverwritingExistingKey_ReplacesOldMapping()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var key = "shared-key";

        provider.RegisterApiKey(key, "User1");
        provider.RegisterApiKey(key, "User2"); // Overwrite

        var credentials = new AuthenticationCredentials("ApiKey", key);

        // Act
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("User2", principal.Identity?.Name); // Should have new name
    }

    [Fact]
    public async Task AuthenticateAsync_WithCaseSensitiveKey_ReturnsDifferentResults()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        provider.RegisterApiKey("LowerCase", "User1");
        provider.RegisterApiKey("lowercase", "User2");

        var creds1 = new AuthenticationCredentials("ApiKey", "LowerCase");
        var creds2 = new AuthenticationCredentials("ApiKey", "lowercase");

        // Act
        var principal1 = await provider.AuthenticateAsync(creds1);
        var principal2 = await provider.AuthenticateAsync(creds2);

        // Assert - Keys should be case-sensitive
        Assert.NotNull(principal1);
        Assert.NotNull(principal2);
        Assert.Equal("User1", principal1.Identity?.Name);
        Assert.Equal("User2", principal2.Identity?.Name);
    }

    [Fact]
    public async Task AuthenticateAsync_WithSchemeCaseMismatch_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider("ApiKey");
        provider.RegisterApiKey("test", "User");

        var credentials = new AuthenticationCredentials("apikey", "test"); // lowercase scheme

        // Act
        var principal = await provider.AuthenticateAsync(credentials);

        // Assert - Scheme comparison should be case-sensitive
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithNullToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var principal = await provider.ValidateTokenAsync(null!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var principal = await provider.ValidateTokenAsync("");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithWhitespaceToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();

        // Act
        var principal = await provider.ValidateTokenAsync("   ");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsPrincipal()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var token = "valid-token";
        provider.RegisterApiKey(token, "TokenUser");

        // Act
        var principal = await provider.ValidateTokenAsync(token);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("TokenUser", principal.Identity?.Name);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ReturnsNull()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        provider.RegisterApiKey("valid-token", "User");

        // Act
        var principal = await provider.ValidateTokenAsync("invalid-token");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public void RegisterApiKey_WithNoClaims_CreatesIdentityWithOnlyNameClaim()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var key = "minimal-key";
        var name = "MinimalUser";

        // Act
        provider.RegisterApiKey(key, name);
        var credentials = new AuthenticationCredentials("ApiKey", key);
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(name, principal.Identity?.Name);
        Assert.True(principal.Identity?.IsAuthenticated);
        // Should have at least the name claim
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Name && c.Value == name);
    }

    [Fact]
    public void RegisterApiKey_WithEmptyClaims_CreatesIdentityWithOnlyNameClaim()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var key = "empty-claims-key";
        var name = "EmptyClaimsUser";
        var emptyClaims = Array.Empty<Claim>();

        // Act
        provider.RegisterApiKey(key, name, emptyClaims);
        var credentials = new AuthenticationCredentials("ApiKey", key);
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(name, principal.Identity?.Name);
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Name && c.Value == name);
    }

    [Fact]
    public void RegisterApiKey_WithDuplicateClaims_AllClaimsAreStored()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, "user"),
            new Claim(ClaimTypes.Role, "admin"), // Duplicate type, different value
            new Claim(ClaimTypes.Role, "manager")
        };

        // Act
        provider.RegisterApiKey("multi-role-key", "MultiRoleUser", claims);
        var credentials = new AuthenticationCredentials("ApiKey", "multi-role-key");
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        var roleClaims = principal.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
        Assert.Equal(3, roleClaims.Count);
        Assert.Contains(roleClaims, c => c.Value == "user");
        Assert.Contains(roleClaims, c => c.Value == "admin");
        Assert.Contains(roleClaims, c => c.Value == "manager");
    }

    [Fact]
    public async Task AuthenticateAsync_ConcurrentAccess_ReturnsCorrectPrincipals()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var keys = Enumerable.Range(1, 100).Select(i => ($"key-{i}", $"user-{i}")).ToList();

        foreach (var (key, name) in keys)
        {
            provider.RegisterApiKey(key, name);
        }

        // Act - Concurrent authentication
        var tasks = keys.Select(async kv =>
        {
            var credentials = new AuthenticationCredentials("ApiKey", kv.Item1);
            var principal = await provider.AuthenticateAsync(credentials);
            return (ExpectedName: kv.Item2, ActualName: principal?.Identity?.Name);
        });

        var results = await Task.WhenAll(tasks);

        // Assert - All should match
        foreach (var (ExpectedName, ActualName) in results)
        {
            Assert.Equal(ExpectedName, ActualName);
        }
    }

    [Fact]
    public void RegisterApiKey_WithSpecialCharactersInKey_WorksCorrectly()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var specialKey = "key!@#$%^&*()_+-=[]{}|;':\",./<>?";

        // Act
        provider.RegisterApiKey(specialKey, "SpecialUser");
        var credentials = new AuthenticationCredentials("ApiKey", specialKey);
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("SpecialUser", principal.Identity?.Name);
    }

    [Fact]
    public void RegisterApiKey_WithUnicodeCharactersInName_WorksCorrectly()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var unicodeName = "用户名 🔒 Пользователь";

        // Act
        provider.RegisterApiKey("unicode-key", unicodeName);
        var credentials = new AuthenticationCredentials("ApiKey", "unicode-key");
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(unicodeName, principal.Identity?.Name);
    }

    [Fact]
    public void RegisterApiKey_WithVeryLongKey_WorksCorrectly()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var longKey = new string('a', 10000); // 10KB key

        // Act
        provider.RegisterApiKey(longKey, "LongKeyUser");
        var credentials = new AuthenticationCredentials("ApiKey", longKey);
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal("LongKeyUser", principal.Identity?.Name);
    }

    [Fact]
    public void RegisterApiKey_WithVeryLongName_WorksCorrectly()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var longName = new string('b', 10000); // 10KB name

        // Act
        provider.RegisterApiKey("long-name-key", longName);
        var credentials = new AuthenticationCredentials("ApiKey", "long-name-key");
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(longName, principal.Identity?.Name);
    }

    [Fact]
    public void RegisterApiKey_WithManyClaimsTypes_AllArePreserved()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, "user@example.com"),
            new Claim(ClaimTypes.GivenName, "John"),
            new Claim(ClaimTypes.Surname, "Doe"),
            new Claim(ClaimTypes.Country, "US"),
            new Claim(ClaimTypes.DateOfBirth, "1990-01-01"),
            new Claim(ClaimTypes.MobilePhone, "+1234567890"),
            new Claim("department", "Engineering"),
            new Claim("level", "senior"),
            new Claim("permissions", "read"),
            new Claim("permissions", "write")
        };

        // Act
        provider.RegisterApiKey("complex-key", "ComplexUser", claims);
        var credentials = new AuthenticationCredentials("ApiKey", "complex-key");
        var principal = provider.AuthenticateAsync(credentials).Result;

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(10, principal.Claims.Count(c => c.Type != ClaimTypes.Name)); // Excluding added Name claim
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Email && c.Value == "user@example.com");
        Assert.Contains(principal.Claims, c => c.Type == "department" && c.Value == "Engineering");
    }

    [Fact]
    public async Task AuthenticateAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        provider.RegisterApiKey("test-key", "User");
        var credentials = new AuthenticationCredentials("ApiKey", "test-key");
        var cts = new CancellationTokenSource();

        // Act
        var principal = await provider.AuthenticateAsync(credentials, cts.Token);

        // Assert
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        provider.RegisterApiKey("token", "User");
        var cts = new CancellationTokenSource();

        // Act
        var principal = await provider.ValidateTokenAsync("token", cts.Token);

        // Assert
        Assert.NotNull(principal);
    }

    [Fact]
    public void RegisterApiKey_CalledManyTimes_AllKeysAreAccessible()
    {
        // Arrange
        var provider = new ClaimsAuthenticationProvider();
        var keyCount = 1000;

        for (int i = 0; i < keyCount; i++)
        {
            provider.RegisterApiKey($"key-{i}", $"user-{i}");
        }

        // Act & Assert - Verify all keys work
        for (int i = 0; i < keyCount; i++)
        {
            var credentials = new AuthenticationCredentials("ApiKey", $"key-{i}");
            var principal = provider.AuthenticateAsync(credentials).Result;

            Assert.NotNull(principal);
            Assert.Equal($"user-{i}", principal.Identity?.Name);
        }
    }
}
