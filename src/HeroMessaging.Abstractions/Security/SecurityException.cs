namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Base exception for security-related errors
/// </summary>
public class SecurityException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityException"/> class.
    /// </summary>
    public SecurityException(string message) : base(message)
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityException"/> class.
    /// </summary>

    public SecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when encryption or decryption fails
/// </summary>
public class EncryptionException : SecurityException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptionException"/> class.
    /// </summary>
    public EncryptionException(string message) : base(message)
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptionException"/> class.
    /// </summary>

    public EncryptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when signature verification fails
/// </summary>
public class SignatureVerificationException : SecurityException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignatureVerificationException"/> class.
    /// </summary>
    public SignatureVerificationException(string message) : base(message)
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="SignatureVerificationException"/> class.
    /// </summary>

    public SignatureVerificationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : SecurityException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationException"/> class.
    /// </summary>
    public AuthenticationException(string message) : base(message)
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationException"/> class.
    /// </summary>

    public AuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authorization fails
/// </summary>
public class AuthorizationException : SecurityException
{
    /// <summary>
    /// Gets required permission.
    /// </summary>
    public string? RequiredPermission { get; }
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationException"/> class.
    /// </summary>

    public AuthorizationException(string message, string? requiredPermission = null) : base(message)
    {
        RequiredPermission = requiredPermission;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationException"/> class.
    /// </summary>

    public AuthorizationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
