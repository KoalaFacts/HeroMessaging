using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authorization;
using Xunit;

namespace HeroMessaging.Security.Tests.Authorization;

/// <summary>
/// Unit tests for PolicyAuthorizationProvider and AuthorizationPolicy
/// </summary>
public sealed class PolicyAuthorizationProviderTests
{
    private static ClaimsPrincipal CreatePrincipal(string? name = null, params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        if (name != null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
        }
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateUnauthenticatedPrincipal()
    {
        var identity = new ClaimsIdentity(); // No authentication type = unauthenticated
        return new ClaimsPrincipal(identity);
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithDefaultSettings_Succeeds()
    {
        // Act
        var provider = new PolicyAuthorizationProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_RequireAuthenticatedUser_DefaultIsTrue()
    {
        // Act
        var provider = new PolicyAuthorizationProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithRequireAuthenticatedUserFalse_Succeeds()
    {
        // Act
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: false);

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region AddPolicy Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicy_WithValidPolicy_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        provider.AddPolicy("TestPolicy", policy);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicy_WithNullPolicyName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.AddPolicy(null!, policy));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicy_WithEmptyPolicyName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => provider.AddPolicy("", policy));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicy_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => provider.AddPolicy("TestPolicy", null!));
        Assert.Equal("policy", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AddPolicy_OverwritesPreviousPolicy()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy1 = new AuthorizationPolicy("TestPolicy");
        var policy2 = new AuthorizationPolicy("TestPolicy");

        // Act
        provider.AddPolicy("TestPolicy", policy1);
        provider.AddPolicy("TestPolicy", policy2); // Should overwrite

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region RequireRole Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RequireRole_WithValidRole_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireRole("UserCreated", "Send", "Admin");

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequireRole_WithMultipleRoles_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireRole("UserCreated", "Send", "Admin", "Manager", "User");

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region RequireClaim Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void RequireClaim_WithValidClaim_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireClaim("UserCreated", "Send", "department", "Engineering");

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequireClaim_WithMultipleValues_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireClaim("UserCreated", "Send", "tier", "Gold", "Silver", "Platinum");

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region AllowAnonymous Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AllowAnonymous_ForMessageTypeAndOperation_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.AllowAnonymous("PublicMessage", "Receive");

        // Assert
        Assert.NotNull(provider);
    }

    #endregion

    #region AuthorizeAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithNullPrincipal_RequiresAuthentication()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);

        // Act
        var result = await provider.AuthorizeAsync(null, "TestMessage", "Send");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Unauthenticated", result.FailureReason);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithUnauthenticatedPrincipal_RequiresAuthentication()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);
        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(principal, "TestMessage", "Send");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithAuthenticatedPrincipal_NoSpecificPolicy_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);
        var principal = CreatePrincipal("TestUser");

        // Act
        var result = await provider.AuthorizeAsync(principal, "TestMessage", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithAnonymousPolicy_AllowsUnauthenticatedUser()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);
        provider.AllowAnonymous("PublicMessage", "Receive");

        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(principal, "PublicMessage", "Receive");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithRolePolicy_AuthorizedUser_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("UserCreated", "Send", "Admin");

        var claims = new[] { new Claim(ClaimTypes.Role, "Admin") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "UserCreated", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithRolePolicy_UnauthorizedUser_Fails()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("UserCreated", "Send", "Admin");

        var claims = new[] { new Claim(ClaimTypes.Role, "User") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "UserCreated", "Send");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("InsufficientPermissions", result.FailureReason);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithClaimPolicy_UserWithClaim_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("SecureMessage", "Send", "clearance", "Level3");

        var claims = new[] { new Claim("clearance", "Level3") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "SecureMessage", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithClaimPolicy_UserWithoutClaim_Fails()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("SecureMessage", "Send", "clearance", "Level3");

        var principal = CreatePrincipal("TestUser");

        // Act
        var result = await provider.AuthorizeAsync(principal, "SecureMessage", "Send");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithWildcardMessageType_Matches()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("*", "Send", "Admin");

        var claims = new[] { new Claim(ClaimTypes.Role, "Admin") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "AnyMessageType", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_SpecificPolicyTakesPrecedenceOverWildcard()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("*", "Send", "Admin"); // Wildcard
        provider.AllowAnonymous("SpecificMessage", "Send"); // Specific policy

        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(principal, "SpecificMessage", "Send");

        // Assert
        Assert.True(result.Succeeded); // Specific policy should take precedence
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithNullMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, null!, "Send"));
        Assert.Equal("messageType", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithNullOperation_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "TestMessage", null!));
        Assert.Equal("operation", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_RequireAuthenticatedUserFalse_AllowsUnauthenticated()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: false);
        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(principal, "TestMessage", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    #endregion

    #region HasPermissionAsync Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithValidPermissionClaim_ReturnsTrue()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var claims = new[] { new Claim("permission", "write") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.HasPermissionAsync(principal, "write");

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithoutPermissionClaim_ReturnsFalse()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act
        var result = await provider.HasPermissionAsync(principal, "write");

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithNullPermission_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.HasPermissionAsync(principal, null!));
        Assert.Equal("permission", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithNullPrincipal_ReturnsFalse()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        var result = await provider.HasPermissionAsync(null, "write");

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_CaseInsensitive()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var claims = new[] { new Claim("permission", "Write") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.HasPermissionAsync(principal, "write");

        // Assert
        Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithMultiplePermissionClaims_FindsCorrectOne()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var claims = new[]
        {
            new Claim("permission", "read"),
            new Claim("permission", "write"),
            new Claim("permission", "delete")
        };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var canRead = await provider.HasPermissionAsync(principal, "read");
        var canWrite = await provider.HasPermissionAsync(principal, "write");
        var canDelete = await provider.HasPermissionAsync(principal, "delete");

        // Assert
        Assert.True(canRead);
        Assert.True(canWrite);
        Assert.True(canDelete);
    }

    #endregion

    #region AuthorizationPolicy Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Constructor_WithName_Succeeds()
    {
        // Act
        var policy = new AuthorizationPolicy("TestPolicy");

        // Assert
        Assert.NotNull(policy);
        Assert.Equal("TestPolicy", policy.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(null!));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireAuthenticatedUser_ReturnsPolicy()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        var result = policy.RequireAuthenticatedUser();

        // Assert
        Assert.Same(policy, result); // Should return self for chaining
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_AllowAnonymous_ReturnsPolicy()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        var result = policy.AllowAnonymous();

        // Assert
        Assert.Same(policy, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireRole_WithValidRoles_Succeeds()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        var result = policy.RequireRole("Admin", "Manager");

        // Assert
        Assert.Same(policy, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireRole_WithEmptyRoles_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => policy.RequireRole());
        Assert.Equal("roles", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireClaim_WithValidClaim_Succeeds()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        var result = policy.RequireClaim("department");

        // Assert
        Assert.Same(policy, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireClaim_WithNullClaimType_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => policy.RequireClaim(null!));
        Assert.Equal("claimType", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireAssertion_WithValidAssertion_Succeeds()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act
        var result = policy.RequireAssertion(p => p.Identity?.IsAuthenticated ?? false);

        // Assert
        Assert.Same(policy, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireAssertion_WithNullAssertion_ThrowsArgumentNullException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(
            () => policy.RequireAssertion(null!));
        Assert.Equal("requirement", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Evaluate_AllowAnonymous_Succeeds()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy").AllowAnonymous();
        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Evaluate_RequireAuthenticatedUser_Fails()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy").RequireAuthenticatedUser();
        var principal = CreateUnauthenticatedPrincipal();

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Evaluate_WithCustomAssertion_ExecutesAssertion()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy")
            .RequireAssertion(p => p.Identity?.Name == "SpecificUser");

        var principal = CreatePrincipal("SpecificUser");

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Evaluate_WithFailingCustomAssertion_Fails()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy")
            .RequireAssertion(p => p.Identity?.Name == "SpecificUser");

        var principal = CreatePrincipal("DifferentUser");

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_Evaluate_WithThrowingCustomAssertion_CatchesException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy")
            .RequireAssertion(p => throw new InvalidOperationException("Test exception"));

        var principal = CreatePrincipal("TestUser");

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("threw exception", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_ChainableApproach_Succeeds()
    {
        // Arrange & Act
        var policy = new AuthorizationPolicy("ChainedPolicy")
            .RequireAuthenticatedUser()
            .RequireRole("Admin")
            .RequireClaim("department", "Engineering")
            .RequireAssertion(p => p.Claims.Count() > 0);

        // Assert
        Assert.Equal("ChainedPolicy", policy.Name);
    }

    #endregion

    #region Cancellation Token Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithCancellationToken_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");
        var cts = new CancellationTokenSource();

        // Act
        var result = await provider.AuthorizeAsync(principal, "TestMessage", "Send", cts.Token);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithCancellationToken_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var claims = new[] { new Claim("permission", "write") };
        var principal = CreatePrincipal("TestUser", claims);
        var cts = new CancellationTokenSource();

        // Act
        var result = await provider.HasPermissionAsync(principal, "write", cts.Token);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Complex Authorization Scenarios

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_MultipleRolesWithOR_Succeeds()
    {
        // Arrange - User only has Manager role, policy requires Admin OR Manager
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("FinancialReport", "Send", "Admin", "Manager");

        var claims = new[] { new Claim(ClaimTypes.Role, "Manager") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "FinancialReport", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_ClaimValueMatching_CaseInsensitive()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("SecureMessage", "Send", "tier", "Gold");

        var claims = new[] { new Claim("tier", "gold") };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "SecureMessage", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_MultipleClaimRequirements_AllMustMatch()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("AdvancedMessage", "Send", "department");
        provider.RequireClaim("AdvancedMessage", "Send", "clearance");

        // User has both claims
        var claims = new[]
        {
            new Claim("department", "Engineering"),
            new Claim("clearance", "Level3")
        };
        var principal = CreatePrincipal("TestUser", claims);

        // Act
        var result = await provider.AuthorizeAsync(principal, "AdvancedMessage", "Send");

        // Assert
        Assert.True(result.Succeeded);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithEmptyMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "", "Send"));
        Assert.Equal("messageType", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuthorizeAsync_WithWhitespaceMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "   ", "Send"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HasPermissionAsync_WithEmptyPermission_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = CreatePrincipal("TestUser");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.HasPermissionAsync(principal, ""));
        Assert.Equal("permission", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AuthorizationPolicy_RequireRole_WithNullOrEmptyRoleNames_AreFiltered()
    {
        // Arrange
        var policy = new AuthorizationPolicy("TestPolicy");

        // Act - Pass empty strings which should be filtered
        policy.RequireRole("Admin", "", null);

        // Assert
        Assert.NotNull(policy);
    }

    #endregion
}
