namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Base exception for security-related errors
/// </summary>
public class SecurityException : Exception
{
    public SecurityException(string message) : base(message)
    {
    }

    public SecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when encryption or decryption fails
/// </summary>
public class EncryptionException : SecurityException
{
    public EncryptionException(string message) : base(message)
    {
    }

    public EncryptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when signature verification fails
/// </summary>
public class SignatureVerificationException : SecurityException
{
    public SignatureVerificationException(string message) : base(message)
    {
    }

    public SignatureVerificationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : SecurityException
{
    public AuthenticationException(string message) : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authorization fails
/// </summary>
public class AuthorizationException : SecurityException
{
    public string? RequiredPermission { get; }

    public AuthorizationException(string message, string? requiredPermission = null) : base(message)
    {
        RequiredPermission = requiredPermission;
    }

    public AuthorizationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
