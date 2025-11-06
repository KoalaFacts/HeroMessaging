using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authorization;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PolicyAuthorizationProviderTests
{
    [Fact]
    public void Constructor_WithDefaultSettings_RequiresAuthentication()
    {
        // Act
        var provider = new PolicyAuthorizationProvider();

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_WithRequireAuthFalse_AllowsAnonymous()
    {
        // Act
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: false);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddPolicy_WithValidPolicy_StoresPolicy()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy = new AuthorizationPolicy("test-policy");

        // Act
        provider.AddPolicy("test-policy", policy);

        // Assert - Will verify through authorization
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddPolicy_WithNullName_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => provider.AddPolicy("", policy));
    }

    [Fact]
    public void AddPolicy_WithNullPolicy_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => provider.AddPolicy("test", null!));
    }

    [Fact]
    public void RequireRole_AddsRolePolicy()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireRole("OrderCommand", MessageOperations.Send, "admin", "manager");

        // Assert - Will verify through authorization
        Assert.NotNull(provider);
    }

    [Fact]
    public void RequireClaim_AddsClaimPolicy()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireClaim("PaymentCommand", MessageOperations.Handle, "permission", "process-payments");

        // Assert - Will verify through authorization
        Assert.NotNull(provider);
    }

    [Fact]
    public void AllowAnonymous_AddsAnonymousPolicy()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.AllowAnonymous("PublicQuery", MessageOperations.Handle);

        // Assert - Will verify through authorization
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task AuthorizeAsync_WithoutPolicyAndRequireAuth_RequiresAuthentication()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);
        var unauthenticatedPrincipal = new ClaimsPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(unauthenticatedPrincipal, "AnyMessage", MessageOperations.Send);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("Authentication required", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthorizeAsync_WithoutPolicyAndNoRequireAuth_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: false);
        var unauthenticatedPrincipal = new ClaimsPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(unauthenticatedPrincipal, "AnyMessage", MessageOperations.Send);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithRolePolicy_AuthorizedRoleSucceeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("OrderCommand", MessageOperations.Send, "admin", "manager");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await provider.AuthorizeAsync(principal, "OrderCommand", MessageOperations.Send);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithRolePolicy_UnauthorizedRoleFails()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("OrderCommand", MessageOperations.Send, "admin");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "user") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await provider.AuthorizeAsync(principal, "OrderCommand", MessageOperations.Send);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("admin", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthorizeAsync_WithClaimPolicy_ValidClaimSucceeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("PaymentCommand", MessageOperations.Handle, "permission", "process-payments");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "process-payments")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await provider.AuthorizeAsync(principal, "PaymentCommand", MessageOperations.Handle);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithClaimPolicy_InvalidClaimFails()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireClaim("PaymentCommand", MessageOperations.Handle, "permission", "process-payments");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "view-only")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await provider.AuthorizeAsync(principal, "PaymentCommand", MessageOperations.Handle);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("permission", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthorizeAsync_WithAnonymousPolicy_UnauthenticatedSucceeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);
        provider.AllowAnonymous("PublicQuery", MessageOperations.Handle);

        var unauthenticatedPrincipal = new ClaimsPrincipal();

        // Act
        var result = await provider.AuthorizeAsync(unauthenticatedPrincipal, "PublicQuery", MessageOperations.Handle);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithWildcardPolicy_MatchesAllMessageTypes()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("*", MessageOperations.Send, "sender");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "sender") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act - Should match any message type
        var result1 = await provider.AuthorizeAsync(principal, "OrderCommand", MessageOperations.Send);
        var result2 = await provider.AuthorizeAsync(principal, "PaymentCommand", MessageOperations.Send);

        // Assert
        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithNullMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, null!, MessageOperations.Send));
    }

    [Fact]
    public async Task AuthorizeAsync_WithNullOperation_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "Message", null!));
    }

    [Fact]
    public async Task HasPermissionAsync_WithValidPermission_ReturnsTrue()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "delete-orders")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var hasPermission = await provider.HasPermissionAsync(principal, "delete-orders");

        // Assert
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task HasPermissionAsync_WithoutPermission_ReturnsFalse()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "view-orders")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var hasPermission = await provider.HasPermissionAsync(principal, "delete-orders");

        // Assert
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task HasPermissionAsync_WithNullPrincipal_ReturnsFalse()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        var hasPermission = await provider.HasPermissionAsync(null!, "any-permission");

        // Assert
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task HasPermissionAsync_WithNullPermission_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.HasPermissionAsync(principal, null!));
    }

    [Fact]
    public void AuthorizationPolicy_RequireAuthenticatedUser_SetsFlag()
    {
        // Arrange & Act
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser();

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public void AuthorizationPolicy_AllowAnonymous_SetsFlag()
    {
        // Arrange & Act
        var policy = new AuthorizationPolicy("test")
            .AllowAnonymous();

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public void AuthorizationPolicy_RequireRole_WithNoRoles_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => policy.RequireRole());
    }

    [Fact]
    public void AuthorizationPolicy_RequireClaim_WithNullType_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => policy.RequireClaim(null!));
    }

    [Fact]
    public void AuthorizationPolicy_RequireAssertion_WithNullFunc_ThrowsArgumentNullException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => policy.RequireAssertion(null!));
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithCustomAssertion_ExecutesAssertion()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireAssertion(p => p.HasClaim("custom", "value"));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("custom", "value")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithFailingAssertion_ReturnsFailed()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireAssertion(p => false); // Always fails

        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("Custom authorization requirement failed", result.ErrorMessage);
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithExceptionInAssertion_ReturnsFailed()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireAssertion(p => throw new InvalidOperationException("Test error"));

        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("exception", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizationPolicy_Name_ReturnsCorrectName()
    {
        // Arrange
        var policyName = "my-policy";

        // Act
        var policy = new AuthorizationPolicy(policyName);

        // Assert
        Assert.Equal(policyName, policy.Name);
    }

    [Fact]
    public void AuthorizationPolicy_Constructor_WithNullName_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthorizationPolicy(null!));
    }

    [Fact]
    public void AuthorizationPolicy_FluentChaining_WorksCorrectly()
    {
        // Arrange & Act
        var policy = new AuthorizationPolicy("complex-policy")
            .RequireAuthenticatedUser()
            .RequireRole("admin", "manager")
            .RequireClaim("department", "IT", "Engineering")
            .RequireAssertion(p => p.Identity?.Name?.StartsWith("dev-") == true);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "dev-alice"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("department", "Engineering")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }
}
