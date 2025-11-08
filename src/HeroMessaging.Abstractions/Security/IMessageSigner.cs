using System;

namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides message signing and verification for integrity and authenticity
/// </summary>
public interface IMessageSigner
{
    /// <summary>
    /// Signs the message data (async, allocates array)
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
    /// Verifies the message signature (async)
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
    /// Signs the message data to a destination span (zero-allocation synchronous).
    /// Returns the number of bytes written to the signature buffer.
    /// </summary>
    /// <param name="data">The data to sign</param>
    /// <param name="signature">Destination for the signature bytes</param>
    /// <param name="context">Security context</param>
    /// <returns>Number of bytes written to signature</returns>
    int Sign(ReadOnlySpan<byte> data, Span<byte> signature, SecurityContext context);

    /// <summary>
    /// Try to sign the message data to a destination span.
    /// </summary>
    bool TrySign(ReadOnlySpan<byte> data, Span<byte> signature, SecurityContext context, out int bytesWritten);

    /// <summary>
    /// Verifies the message signature from spans (zero-allocation synchronous).
    /// </summary>
    /// <param name="data">The data that was signed</param>
    /// <param name="signature">The signature bytes to verify</param>
    /// <param name="context">Security context</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, SecurityContext context);

    /// <summary>
    /// Gets the signature size in bytes for this algorithm.
    /// </summary>
    int SignatureSize { get; }

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
