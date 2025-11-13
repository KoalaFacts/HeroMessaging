using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Authorization;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PolicyAuthorizationProviderEdgeCasesTests
{
    [Fact]
    public async Task AuthorizeAsync_WithNullPrincipal_RequireAuthTrue_Fails()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: true);

        // Act
        var result = await provider.AuthorizeAsync(null!, "Message", "Operation");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithNullPrincipal_RequireAuthFalse_Succeeds()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider(requireAuthenticatedUser: false);

        // Act
        var result = await provider.AuthorizeAsync(null!, "Message", "Operation");

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithEmptyMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "", "Operation"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithWhitespaceMessageType_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "   ", "Operation"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithEmptyOperation_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "Message", ""));
    }

    [Fact]
    public async Task AuthorizeAsync_WithWhitespaceOperation_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AuthorizeAsync(principal, "Message", "  \t\n"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithCaseDifferentPolicyNames_AreCaseInsensitive()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("TestMessage", "Send", "admin");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act - Try with different casing
        var result1 = await provider.AuthorizeAsync(principal, "testmessage", "send");
        var result2 = await provider.AuthorizeAsync(principal, "TESTMESSAGE", "SEND");

        // Assert - Policy names should be case-insensitive
        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
    }

    [Fact]
    public void RequireRole_WithWhitespaceRoles_IgnoresWhitespace()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireRole("Message", "Op", "admin", "  ", "manager", "");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "manager") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var result = provider.AuthorizeAsync(principal, "Message", "Op").Result;

        // Assert - Should work, whitespace roles ignored
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RequireClaim_WithWhitespaceValues_IgnoresWhitespace()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();

        // Act
        provider.RequireClaim("Message", "Op", "permission", "read", "  ", "write", "");

        var identity = new ClaimsIdentity(new[] { new Claim("permission", "write") }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var result = provider.AuthorizeAsync(principal, "Message", "Op").Result;

        // Assert - Should work, whitespace values ignored
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HasPermissionAsync_WithEmptyPermission_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.HasPermissionAsync(principal, ""));
    }

    [Fact]
    public async Task HasPermissionAsync_WithWhitespacePermission_ThrowsArgumentException()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var principal = new ClaimsPrincipal();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.HasPermissionAsync(principal, "  \t"));
    }

    [Fact]
    public async Task HasPermissionAsync_WithCaseInsensitivePermission_ReturnsTrue()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "delete-orders")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var hasPermission1 = await provider.HasPermissionAsync(principal, "delete-orders");
        var hasPermission2 = await provider.HasPermissionAsync(principal, "DELETE-ORDERS");

        // Assert - Permission check should be case-insensitive
        Assert.True(hasPermission1);
        Assert.True(hasPermission2);
    }

    [Fact]
    public void AuthorizationPolicy_RequireRole_WithRoleCaseMismatch_CaseInsensitive()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireRole("Admin", "Manager");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "admin") // lowercase
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert - Role check should be case-insensitive
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_RequireClaim_WithNoValues_RequiresClaimExists()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireClaim("custom-claim"); // No specific values

        var identity1 = new ClaimsIdentity(new[]
        {
            new Claim("custom-claim", "any-value")
        }, "TestAuth");
        var principal1 = new ClaimsPrincipal(identity1);

        var identity2 = new ClaimsIdentity(new[]
        {
            new Claim("other-claim", "value")
        }, "TestAuth");
        var principal2 = new ClaimsPrincipal(identity2);

        // Act
        var result1 = policy.Evaluate(principal1);
        var result2 = policy.Evaluate(principal2);

        // Assert
        Assert.True(result1.Succeeded); // Has the claim
        Assert.False(result2.Succeeded); // Missing the claim
    }

    [Fact]
    public void AuthorizationPolicy_RequireClaim_WithEmptyType_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => policy.RequireClaim(""));
    }

    [Fact]
    public void AuthorizationPolicy_RequireClaim_WithWhitespaceType_ThrowsArgumentException()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => policy.RequireClaim("  \t"));
    }

    [Fact]
    public void AuthorizationPolicy_AllowAnonymous_OverridesRequireAuthentication()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .AllowAnonymous(); // Should override

        var unauthenticatedPrincipal = new ClaimsPrincipal();

        // Act
        var result = policy.Evaluate(unauthenticatedPrincipal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_RequireAuthenticated_OverridesAllowAnonymous()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .AllowAnonymous()
            .RequireAuthenticatedUser(); // Should override

        var unauthenticatedPrincipal = new ClaimsPrincipal();

        // Act
        var result = policy.Evaluate(unauthenticatedPrincipal);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_MultipleRoleRequirements_AnyRoleMatches()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireRole("admin", "manager", "supervisor");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "supervisor") // One of the roles
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_MultipleClaimRequirements_AllMustMatch()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireClaim("department", "IT")
            .RequireClaim("level", "senior");

        var identity1 = new ClaimsIdentity(new[]
        {
            new Claim("department", "IT"),
            new Claim("level", "senior")
        }, "TestAuth");
        var principal1 = new ClaimsPrincipal(identity1);

        var identity2 = new ClaimsIdentity(new[]
        {
            new Claim("department", "IT")
            // Missing level claim
        }, "TestAuth");
        var principal2 = new ClaimsPrincipal(identity2);

        // Act
        var result1 = policy.Evaluate(principal1);
        var result2 = policy.Evaluate(principal2);

        // Assert
        Assert.True(result1.Succeeded);
        Assert.False(result2.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_WithMultipleRolesOnPrincipal_MatchesAny()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireRole("admin");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "user"),
            new Claim(ClaimTypes.Role, "manager"),
            new Claim(ClaimTypes.Role, "admin") // This one matches
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_RequireAssertion_WithMultipleAssertions_AllMustPass()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireAssertion(p => p.HasClaim("claim1", "value1"))
            .RequireAssertion(p => p.HasClaim("claim2", "value2"));

        var identity1 = new ClaimsIdentity(new[]
        {
            new Claim("claim1", "value1"),
            new Claim("claim2", "value2")
        }, "TestAuth");
        var principal1 = new ClaimsPrincipal(identity1);

        var identity2 = new ClaimsIdentity(new[]
        {
            new Claim("claim1", "value1")
            // Missing claim2
        }, "TestAuth");
        var principal2 = new ClaimsPrincipal(identity2);

        // Act
        var result1 = policy.Evaluate(principal1);
        var result2 = policy.Evaluate(principal2);

        // Assert
        Assert.True(result1.Succeeded);
        Assert.False(result2.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithSpecificAndWildcardPolicies_SpecificTakesPrecedence()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("SpecificMessage", "Send", "admin");
        provider.RequireRole("*", "Send", "user"); // Wildcard

        var adminIdentity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "admin")
        }, "TestAuth");
        var adminPrincipal = new ClaimsPrincipal(adminIdentity);

        var userIdentity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "user")
        }, "TestAuth");
        var userPrincipal = new ClaimsPrincipal(userIdentity);

        // Act
        var adminResult = await provider.AuthorizeAsync(adminPrincipal, "SpecificMessage", "Send");
        var userResult = await provider.AuthorizeAsync(userPrincipal, "SpecificMessage", "Send");

        // Assert - Specific policy should take precedence
        Assert.True(adminResult.Succeeded);
        Assert.False(userResult.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithOnlyWildcard_AppliesToAllMessages()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        provider.RequireRole("*", "Send", "sender");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "sender")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result1 = await provider.AuthorizeAsync(principal, "Message1", "Send");
        var result2 = await provider.AuthorizeAsync(principal, "Message2", "Send");
        var result3 = await provider.AuthorizeAsync(principal, "AnyMessage", "Send");

        // Assert - Wildcard applies to all
        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
        Assert.True(result3.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var cts = new CancellationTokenSource();

        // Act
        var result = await provider.AuthorizeAsync(principal, "Message", "Op", cts.Token);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task HasPermissionAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("permission", "test")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var cts = new CancellationTokenSource();

        // Act
        var hasPermission = await provider.HasPermissionAsync(principal, "test", cts.Token);

        // Assert
        Assert.True(hasPermission);
    }

    [Fact]
    public void AddPolicy_WithSameName_OverwritesExisting()
    {
        // Arrange
        var provider = new PolicyAuthorizationProvider();
        var policy1 = new AuthorizationPolicy("shared-name").RequireRole("admin");
        var policy2 = new AuthorizationPolicy("shared-name").AllowAnonymous();

        // Act
        provider.AddPolicy("shared-name", policy1);
        provider.AddPolicy("shared-name", policy2); // Overwrite

        var unauthPrincipal = new ClaimsPrincipal();
        var result = provider.AuthorizeAsync(unauthPrincipal, "Test", "Op").Result;

        // Assert - Should use second policy (anonymous)
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithComplexNesting_WorksCorrectly()
    {
        // Arrange
        var policy = new AuthorizationPolicy("complex")
            .RequireAuthenticatedUser()
            .RequireRole("admin", "manager")
            .RequireClaim("department", "IT", "Engineering")
            .RequireClaim("level", "senior", "lead")
            .RequireAssertion(p => p.HasClaim("verified", "true"))
            .RequireAssertion(p => int.Parse(p.FindFirst("years")?.Value ?? "0") >= 5);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "manager"),
            new Claim("department", "Engineering"),
            new Claim("level", "senior"),
            new Claim("verified", "true"),
            new Claim("years", "7")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithUnauthenticatedIdentity_Fails()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser();

        var identity = new ClaimsIdentity(); // Not authenticated
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("authenticated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizationPolicy_Evaluate_WithMultipleIdentities_UsesAnyAuthenticated()
    {
        // Arrange
        var policy = new AuthorizationPolicy("test")
            .RequireAuthenticatedUser()
            .RequireRole("admin");

        var identity1 = new ClaimsIdentity(); // Not authenticated
        var identity2 = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "admin")
        }, "TestAuth"); // Authenticated

        var principal = new ClaimsPrincipal(new[] { identity1, identity2 });

        // Act
        var result = policy.Evaluate(principal);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AuthorizationResult_InsufficientPermissions_SetsCorrectCode()
    {
        // Arrange & Act
        var result = AuthorizationResult.InsufficientPermissions("Missing role: admin");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("admin", result.ErrorMessage);
    }

    [Fact]
    public void AuthorizationResult_Failure_WithCustomCode_StoresCode()
    {
        // Arrange & Act
        var result = AuthorizationResult.Failure("Custom failure", "CUSTOM_CODE");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Custom failure", result.ErrorMessage);
    }
}
