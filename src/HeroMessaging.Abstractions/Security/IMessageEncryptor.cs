namespace HeroMessaging.Abstractions.Security;

/// <summary>
/// Provides encryption and decryption capabilities for message payloads
/// </summary>
public interface IMessageEncryptor
{
    /// <summary>
    /// Encrypts the message payload
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
    /// Decrypts the message payload
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
