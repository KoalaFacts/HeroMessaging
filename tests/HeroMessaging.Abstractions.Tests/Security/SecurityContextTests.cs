using System.Security.Claims;
using HeroMessaging.Abstractions.Security;
using Microsoft.Extensions.Time.Testing;

namespace HeroMessaging.Abstractions.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityContextTests
{
    [Fact]
    public void Constructor_InitializesDefaults()
    {
        // Arrange & Act
        var context = new SecurityContext(TimeProvider.System);

        // Assert
        Assert.Null(context.Principal);
        Assert.Null(context.MessageId);
        Assert.Null(context.MessageType);
        Assert.Null(context.CorrelationId);
        Assert.NotNull(context.Metadata);
        Assert.Empty(context.Metadata);
        Assert.NotEqual(default(DateTimeOffset), context.Timestamp);
    }

    [Fact]
    public void Timestamp_IsSetToUtcTime()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero));

        // Act
        var context = new SecurityContext(fakeTime);

        // Assert
        Assert.Equal(fakeTime.GetUtcNow(), context.Timestamp);
    }

    [Fact]
    public void Principal_CanBeSet()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Assert
        Assert.Same(principal, context.Principal);
    }

    [Fact]
    public void MessageId_CanBeSet()
    {
        // Arrange & Act
        var context = new SecurityContext(TimeProvider.System) { MessageId = "msg-123" };

        // Assert
        Assert.Equal("msg-123", context.MessageId);
    }

    [Fact]
    public void MessageType_CanBeSet()
    {
        // Arrange & Act
        var context = new SecurityContext(TimeProvider.System) { MessageType = "MyCommand" };

        // Assert
        Assert.Equal("MyCommand", context.MessageType);
    }

    [Fact]
    public void CorrelationId_CanBeSet()
    {
        // Arrange & Act
        var context = new SecurityContext(TimeProvider.System) { CorrelationId = "corr-456" };

        // Assert
        Assert.Equal("corr-456", context.CorrelationId);
    }

    [Fact]
    public void Metadata_CanBePopulated()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 123 }
        };

        // Act
        var context = new SecurityContext(TimeProvider.System) { Metadata = metadata };

        // Assert
        Assert.Equal(2, context.Metadata.Count);
        Assert.Equal("value1", context.Metadata["key1"]);
        Assert.Equal(123, context.Metadata["key2"]);
    }

    [Fact]
    public void IsAuthenticated_WithNullPrincipal_ReturnsFalse()
    {
        // Arrange
        var context = new SecurityContext(TimeProvider.System) { Principal = null };

        // Act
        var result = context.IsAuthenticated;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAuthenticated_WithUnauthenticatedPrincipal_ReturnsFalse()
    {
        // Arrange
        var identity = new ClaimsIdentity(); // Not authenticated
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var result = context.IsAuthenticated;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAuthenticated_WithAuthenticatedPrincipal_ReturnsTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var result = context.IsAuthenticated;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void PrincipalName_WithNullPrincipal_ReturnsNull()
    {
        // Arrange
        var context = new SecurityContext(TimeProvider.System) { Principal = null };

        // Act
        var name = context.PrincipalName;

        // Assert
        Assert.Null(name);
    }

    [Fact]
    public void PrincipalName_WithNamedPrincipal_ReturnsName()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Name, "TestUser"));
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var name = context.PrincipalName;

        // Assert
        Assert.Equal("TestUser", name);
    }

    [Fact]
    public void HasClaim_WithNullPrincipal_ReturnsFalse()
    {
        // Arrange
        var context = new SecurityContext(TimeProvider.System) { Principal = null };

        // Act
        var result = context.HasClaim(ClaimTypes.Role);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasClaim_WithExistingClaimType_ReturnsTrue()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var result = context.HasClaim(ClaimTypes.Role);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasClaim_WithNonExistingClaimType_ReturnsFalse()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var result = context.HasClaim(ClaimTypes.Role);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasClaim_WithTypeAndValue_MatchesExactly()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var hasAdmin = context.HasClaim(ClaimTypes.Role, "Admin");
        var hasUser = context.HasClaim(ClaimTypes.Role, "User");

        // Assert
        Assert.True(hasAdmin);
        Assert.False(hasUser);
    }

    [Fact]
    public void GetClaims_WithNullPrincipal_ReturnsEmpty()
    {
        // Arrange
        var context = new SecurityContext(TimeProvider.System) { Principal = null };

        // Act
        var claims = context.GetClaims(ClaimTypes.Role);

        // Assert
        Assert.Empty(claims);
    }

    [Fact]
    public void GetClaims_WithMultipleClaims_ReturnsAll()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "User"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Developer"));
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var roles = context.GetClaims(ClaimTypes.Role).ToList();

        // Assert
        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, c => c.Value == "Admin");
        Assert.Contains(roles, c => c.Value == "User");
        Assert.Contains(roles, c => c.Value == "Developer");
    }

    [Fact]
    public void GetClaims_WithNonExistingType_ReturnsEmpty()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var claims = context.GetClaims("NonExistingType");

        // Assert
        Assert.Empty(claims);
    }

    [Fact]
    public void RecordEquality_WorksCorrectly()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
        var timestamp = DateTimeOffset.UtcNow;

        var context1 = new SecurityContext(TimeProvider.System)
        {
            Principal = principal,
            MessageId = "msg-123",
            Timestamp = timestamp
        };

        var context2 = new SecurityContext(TimeProvider.System)
        {
            Principal = principal,
            MessageId = "msg-123",
            Timestamp = timestamp
        };

        // Act & Assert
        Assert.Equal(context1, context2);
    }

    [Fact]
    public void WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new SecurityContext(TimeProvider.System) { MessageId = "original" };

        // Act
        var modified = original with { MessageId = "modified" };

        // Assert
        Assert.Equal("original", original.MessageId);
        Assert.Equal("modified", modified.MessageId);
    }

    [Fact]
    public void ComplexSecurityContext_AllPropertiesSet()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Name, "TestUser"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        var principal = new ClaimsPrincipal(identity);

        var metadata = new Dictionary<string, object>
        {
            { "IpAddress", "192.168.1.1" },
            { "SessionId", "session-123" }
        };

        // Act
        var context = new SecurityContext(TimeProvider.System)
        {
            Principal = principal,
            MessageId = "msg-123",
            MessageType = "SecureCommand",
            CorrelationId = "corr-456",
            Metadata = metadata,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        Assert.True(context.IsAuthenticated);
        Assert.Equal("TestUser", context.PrincipalName);
        Assert.True(context.HasClaim(ClaimTypes.Role, "Admin"));
        Assert.Equal("msg-123", context.MessageId);
        Assert.Equal("SecureCommand", context.MessageType);
        Assert.Equal("corr-456", context.CorrelationId);
        Assert.Equal(2, context.Metadata.Count);
    }

    [Fact]
    public void Metadata_CanStoreVariousTypes()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            { "string", "value" },
            { "int", 42 },
            { "bool", true },
            { "guid", Guid.NewGuid() },
            { "datetime", DateTimeOffset.UtcNow }
        };

        // Act
        var context = new SecurityContext(TimeProvider.System) { Metadata = metadata };

        // Assert
        Assert.Equal(5, context.Metadata.Count);
        Assert.IsType<string>(context.Metadata["string"]);
        Assert.IsType<int>(context.Metadata["int"]);
        Assert.IsType<bool>(context.Metadata["bool"]);
        Assert.IsType<Guid>(context.Metadata["guid"]);
        Assert.IsType<DateTimeOffset>(context.Metadata["datetime"]);
    }

    [Fact]
    public void MultiplePrincipalIdentities_FirstIdentityUsed()
    {
        // Arrange
        var identity1 = new ClaimsIdentity("TestAuth1");
        identity1.AddClaim(new Claim(ClaimTypes.Name, "User1"));
        var identity2 = new ClaimsIdentity("TestAuth2");
        identity2.AddClaim(new Claim(ClaimTypes.Name, "User2"));
        var principal = new ClaimsPrincipal(new[] { identity1, identity2 });
        var context = new SecurityContext(TimeProvider.System) { Principal = principal };

        // Act
        var name = context.PrincipalName;

        // Assert
        Assert.Equal("User1", name);
    }

    [Fact]
    public void Constructor_WithFakeTimeProvider_UsesProvidedTime()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var fakeTimeProvider = new FakeTimeProvider(fixedTime);

        // Act
        var context = new SecurityContext(fakeTimeProvider);

        // Assert
        Assert.Equal(fixedTime, context.Timestamp);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SecurityContext(null!));
    }
}
