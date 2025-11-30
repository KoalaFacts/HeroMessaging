using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Abstractions.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityExceptionTests
{
    [Fact]
    public void SecurityException_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new SecurityException("Test security error");

        // Assert
        Assert.Equal("Test security error", exception.Message);
    }

    [Fact]
    public void SecurityException_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner exception");

        // Act
        var exception = new SecurityException("Security error", inner);

        // Assert
        Assert.Equal("Security error", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void SecurityException_InheritsFromException()
    {
        // Arrange & Act
        var exception = new SecurityException("Test");

        // Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void EncryptionException_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new EncryptionException("Encryption failed");

        // Assert
        Assert.Equal("Encryption failed", exception.Message);
    }

    [Fact]
    public void EncryptionException_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Crypto error");

        // Act
        var exception = new EncryptionException("Encryption failed", inner);

        // Assert
        Assert.Equal("Encryption failed", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void EncryptionException_InheritsFromSecurityException()
    {
        // Arrange & Act
        var exception = new EncryptionException("Test");

        // Assert
        Assert.IsAssignableFrom<SecurityException>(exception);
    }

    [Fact]
    public void SignatureVerificationException_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new SignatureVerificationException("Signature invalid");

        // Assert
        Assert.Equal("Signature invalid", exception.Message);
    }

    [Fact]
    public void SignatureVerificationException_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Hash mismatch");

        // Act
        var exception = new SignatureVerificationException("Signature invalid", inner);

        // Assert
        Assert.Equal("Signature invalid", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void SignatureVerificationException_InheritsFromSecurityException()
    {
        // Arrange & Act
        var exception = new SignatureVerificationException("Test");

        // Assert
        Assert.IsAssignableFrom<SecurityException>(exception);
    }

    [Fact]
    public void AuthenticationException_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new AuthenticationException("Authentication failed");

        // Assert
        Assert.Equal("Authentication failed", exception.Message);
    }

    [Fact]
    public void AuthenticationException_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Token expired");

        // Act
        var exception = new AuthenticationException("Authentication failed", inner);

        // Assert
        Assert.Equal("Authentication failed", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void AuthenticationException_InheritsFromSecurityException()
    {
        // Arrange & Act
        var exception = new AuthenticationException("Test");

        // Assert
        Assert.IsAssignableFrom<SecurityException>(exception);
    }

    [Fact]
    public void AuthorizationException_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new AuthorizationException("Not authorized");

        // Assert
        Assert.Equal("Not authorized", exception.Message);
        Assert.Null(exception.RequiredPermission);
    }

    [Fact]
    public void AuthorizationException_WithMessageAndPermission_SetsBoth()
    {
        // Arrange & Act
        var exception = new AuthorizationException("Not authorized", "admin:write");

        // Assert
        Assert.Equal("Not authorized", exception.Message);
        Assert.Equal("admin:write", exception.RequiredPermission);
    }

    [Fact]
    public void AuthorizationException_WithMessageAndInner_SetsBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Permission check failed");

        // Act
        var exception = new AuthorizationException("Not authorized", inner);

        // Assert
        Assert.Equal("Not authorized", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Null(exception.RequiredPermission);
    }

    [Fact]
    public void AuthorizationException_InheritsFromSecurityException()
    {
        // Arrange & Act
        var exception = new AuthorizationException("Test");

        // Assert
        Assert.IsAssignableFrom<SecurityException>(exception);
    }

    [Fact]
    public void AuthorizationException_WithNullPermission_SetsToNull()
    {
        // Arrange & Act
        var exception = new AuthorizationException("Not authorized", requiredPermission: null);

        // Assert
        Assert.Null(exception.RequiredPermission);
    }

    [Fact]
    public void AllExceptions_CanBeThrown()
    {
        // Arrange & Act & Assert
        Assert.Throws<SecurityException>((Action)(() => throw new SecurityException("Test")));
        Assert.Throws<EncryptionException>((Action)(() => throw new EncryptionException("Test")));
        Assert.Throws<SignatureVerificationException>((Action)(() => throw new SignatureVerificationException("Test")));
        Assert.Throws<AuthenticationException>((Action)(() => throw new AuthenticationException("Test")));
        Assert.Throws<AuthorizationException>((Action)(() => throw new AuthorizationException("Test")));
    }

    [Fact]
    public void SecurityException_CanBeCaughtAsBase()
    {
        // Arrange
        var thrown = false;
        bool caught;

        // Act
        try
        {
            thrown = true;
            throw new EncryptionException("Test");
        }
        catch (SecurityException)
        {
            caught = true;
        }

        // Assert
        Assert.True(thrown);
        Assert.True(caught);
    }

    [Fact]
    public void DerivedExceptions_PreserveExceptionHierarchy()
    {
        // Arrange
        var encryption = new EncryptionException("Test");
        var signature = new SignatureVerificationException("Test");
        var authentication = new AuthenticationException("Test");
        var authorization = new AuthorizationException("Test");

        // Act & Assert - All should be catchable as SecurityException
        Assert.IsAssignableFrom<SecurityException>(encryption);
        Assert.IsAssignableFrom<SecurityException>(signature);
        Assert.IsAssignableFrom<SecurityException>(authentication);
        Assert.IsAssignableFrom<SecurityException>(authorization);

        // And as base Exception
        Assert.IsAssignableFrom<Exception>(encryption);
        Assert.IsAssignableFrom<Exception>(signature);
        Assert.IsAssignableFrom<Exception>(authentication);
        Assert.IsAssignableFrom<Exception>(authorization);
    }

    [Fact]
    public void ExceptionFiltering_WorksCorrectly()
    {
        // Arrange & Act
        var encryptionCaught = false;
        var securityCaught = false;

        try
        {
            throw new EncryptionException("Test");
        }
        catch (EncryptionException)
        {
            encryptionCaught = true;
        }
        catch (SecurityException)
        {
            securityCaught = true;
        }

        // Assert - More specific catch should be hit
        Assert.True(encryptionCaught);
        Assert.False(securityCaught);
    }

    [Fact]
    public void AuthorizationException_WithComplexPermission_StoresCorrectly()
    {
        // Arrange & Act
        var exception = new AuthorizationException(
            "User does not have required permission",
            "resource:action:scope");

        // Assert
        Assert.Equal("resource:action:scope", exception.RequiredPermission);
    }

    [Fact]
    public void ExceptionMessages_CanContainDetails()
    {
        // Arrange & Act
        var encryption = new EncryptionException("Failed to encrypt message with algorithm AES-256");
        var signature = new SignatureVerificationException("Signature verification failed: hash mismatch");
        var authentication = new AuthenticationException("Token expired at 2025-01-01 12:00:00");
        var authorization = new AuthorizationException("Access denied to resource /api/admin");

        // Assert
        Assert.Contains("AES-256", encryption.Message);
        Assert.Contains("hash mismatch", signature.Message);
        Assert.Contains("expired", authentication.Message);
        Assert.Contains("/api/admin", authorization.Message);
    }

    [Fact]
    public void InnerExceptions_ChainCorrectly()
    {
        // Arrange
        var root = new InvalidOperationException("Root cause");
        var middle = new SecurityException("Middle layer", root);
        var outer = new EncryptionException("Outer layer", middle);

        // Act & Assert
        Assert.Same(middle, outer.InnerException);
        Assert.Same(root, outer.InnerException?.InnerException);
    }

    [Fact]
    public void ExceptionStackTrace_IsPreserved()
    {
        // Arrange
        SecurityException? caught = null;

        // Act
        try
        {
            ThrowSecurityException();
        }
        catch (SecurityException ex)
        {
            caught = ex;
        }

        // Assert
        Assert.NotNull(caught);
        Assert.NotNull(caught.StackTrace);
        Assert.Contains(nameof(ThrowSecurityException), caught.StackTrace);
    }

    private static void ThrowSecurityException()
    {
        throw new SecurityException("Test exception with stack trace");
    }
}
