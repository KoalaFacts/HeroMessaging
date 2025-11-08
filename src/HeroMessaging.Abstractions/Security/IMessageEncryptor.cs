using System;

namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides encryption and decryption capabilities for message payloads
/// </summary>
public interface IMessageEncryptor
{
    /// <summary>
    /// Encrypts the message payload (async, allocates arrays)
    /// </summary>
    /// <param name="plaintext">The plaintext data to encrypt</param>
    /// <param name="context">Security context for encryption metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Encrypted data with initialization vector and authentication tag</returns>
    Task<EncryptedData> EncryptAsync(
        byte[] plaintext,
        SecurityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts the message payload (async, allocates array)
    /// </summary>
    /// <param name="encryptedData">The encrypted data to decrypt</param>
    /// <param name="context">Security context for decryption metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted plaintext data</returns>
    Task<byte[]> DecryptAsync(
        EncryptedData encryptedData,
        SecurityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts plaintext to destination spans (zero-allocation synchronous).
    /// Returns the number of bytes written to the ciphertext buffer.
    /// </summary>
    /// <param name="plaintext">The plaintext data to encrypt</param>
    /// <param name="ciphertext">Destination for encrypted data</param>
    /// <param name="iv">Destination for initialization vector</param>
    /// <param name="tag">Destination for authentication tag (if applicable)</param>
    /// <param name="context">Security context</param>
    /// <returns>Number of bytes written to ciphertext</returns>
    int Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> iv, Span<byte> tag, SecurityContext context);

    /// <summary>
    /// Try to encrypt plaintext to destination spans.
    /// </summary>
    bool TryEncrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> iv, Span<byte> tag, SecurityContext context, out int bytesWritten);

    /// <summary>
    /// Decrypts ciphertext from source spans to destination (zero-allocation synchronous).
    /// Returns the number of bytes written to the plaintext buffer.
    /// </summary>
    /// <param name="ciphertext">The encrypted data</param>
    /// <param name="iv">Initialization vector</param>
    /// <param name="tag">Authentication tag (if applicable)</param>
    /// <param name="plaintext">Destination for decrypted data</param>
    /// <param name="context">Security context</param>
    /// <returns>Number of bytes written to plaintext</returns>
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> tag, Span<byte> plaintext, SecurityContext context);

    /// <summary>
    /// Try to decrypt ciphertext from source spans to destination.
    /// </summary>
    bool TryDecrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> tag, Span<byte> plaintext, SecurityContext context, out int bytesWritten);

    /// <summary>
    /// Gets the initialization vector size in bytes for this algorithm.
    /// </summary>
    int IVSize { get; }

    /// <summary>
    /// Gets the authentication tag size in bytes for this algorithm (0 if not applicable).
    /// </summary>
    int TagSize { get; }

    /// <summary>
    /// Gets the encryption algorithm identifier
    /// </summary>
    string Algorithm { get; }
}

/// <summary>
/// Represents encrypted data with metadata
/// </summary>
public sealed class EncryptedData
{
    /// <summary>
    /// The encrypted ciphertext
    /// </summary>
    public byte[] Ciphertext { get; }

    /// <summary>
    /// Initialization vector used for encryption
    /// </summary>
    public byte[] InitializationVector { get; }

    /// <summary>
    /// Authentication tag for authenticated encryption (e.g., GCM mode)
    /// </summary>
    public byte[]? AuthenticationTag { get; }

    /// <summary>
    /// Key identifier for key rotation support
    /// </summary>
    public string? KeyId { get; }

    /// <summary>
    /// Algorithm used for encryption
    /// </summary>
    public string Algorithm { get; }

    public EncryptedData(
        byte[] ciphertext,
        byte[] initializationVector,
        string algorithm,
        byte[]? authenticationTag = null,
        string? keyId = null)
    {
        Ciphertext = ciphertext ?? throw new ArgumentNullException(nameof(ciphertext));
        InitializationVector = initializationVector ?? throw new ArgumentNullException(nameof(initializationVector));
        Algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        AuthenticationTag = authenticationTag;
        KeyId = keyId;
    }
}
