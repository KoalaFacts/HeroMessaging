namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides message signing and verification for integrity and authenticity
/// </summary>
public interface IMessageSigner
{
    /// <summary>
    /// Signs the message data
    /// </summary>
    /// <param name="data">The data to sign</param>
    /// <param name="context">Security context for signing metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Digital signature</returns>
    Task<MessageSignature> SignAsync(
        byte[] data,
        SecurityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the message signature
    /// </summary>
    /// <param name="data">The data that was signed</param>
    /// <param name="signature">The signature to verify</param>
    /// <param name="context">Security context for verification metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    Task<bool> VerifyAsync(
        byte[] data,
        MessageSignature signature,
        SecurityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the signing algorithm identifier
    /// </summary>
    string Algorithm { get; }
}

/// <summary>
/// Represents a message signature with metadata
/// </summary>
public sealed class MessageSignature
{
    /// <summary>
    /// The signature bytes
    /// </summary>
    public byte[] SignatureBytes { get; }

    /// <summary>
    /// Algorithm used for signing
    /// </summary>
    public string Algorithm { get; }

    /// <summary>
    /// Key identifier for key rotation support
    /// </summary>
    public string? KeyId { get; }

    /// <summary>
    /// Timestamp when signature was created
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    public MessageSignature(
        byte[] signatureBytes,
        string algorithm,
        string? keyId = null,
        DateTimeOffset? timestamp = null)
    {
        SignatureBytes = signatureBytes ?? throw new ArgumentNullException(nameof(signatureBytes));
        Algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        KeyId = keyId;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }
}
