using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Encryption;

/// <summary>
/// Provides AES-256-GCM authenticated encryption for messages
/// </summary>
public sealed class AesGcmMessageEncryptor : IMessageEncryptor
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits (recommended for GCM)
    private const int TagSize = 16; // 128 bits

    private readonly byte[] _key;
    private readonly string? _keyId;

    public string Algorithm => "AES-256-GCM";

    /// <summary>
    /// Creates a new AES-GCM encryptor with the specified key
    /// </summary>
    /// <param name="key">The 256-bit encryption key</param>
    /// <param name="keyId">Optional key identifier for key rotation</param>
    public AesGcmMessageEncryptor(byte[] key, string? keyId = null)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes ({KeySize * 8} bits)", nameof(key));

        _key = new byte[key.Length];
        Array.Copy(key, _key, key.Length);
        _keyId = keyId;
    }

    /// <summary>
    /// Creates a new AES-GCM encryptor by generating a random key
    /// </summary>
    public static AesGcmMessageEncryptor CreateWithRandomKey(string? keyId = null)
    {
        var key = new byte[KeySize];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return new AesGcmMessageEncryptor(key, keyId);
    }

    public Task<EncryptedData> EncryptAsync(
        byte[] plaintext,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        if (plaintext == null)
            throw new ArgumentNullException(nameof(plaintext));

        try
        {
            // Generate a random nonce (IV)
            var nonce = new byte[NonceSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

#if NETSTANDARD2_0
            // AesGcm is not available in netstandard2.0
            throw new PlatformNotSupportedException(
                "AES-GCM is not available in .NET Standard 2.0. " +
                "Please target .NET 6.0 or later for AES-GCM support, " +
                "or use AesCbcHmacMessageEncryptor for .NET Standard 2.0 compatibility.");
#elif NET8_0_OR_GREATER
            // .NET 8+ supports tag size in constructor
            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }
#else
            // .NET 6-7 don't support tag size parameter
            using (var aes = new AesGcm(_key))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }
#endif

            var encryptedData = new EncryptedData(
                ciphertext,
                nonce,
                Algorithm,
                tag,
                _keyId);

            return Task.FromResult(encryptedData);
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Failed to encrypt message", ex);
        }
    }

    public Task<byte[]> DecryptAsync(
        EncryptedData encryptedData,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (encryptedData.Algorithm != Algorithm)
            throw new EncryptionException($"Unsupported algorithm: {encryptedData.Algorithm}. Expected: {Algorithm}");

        if (encryptedData.AuthenticationTag == null)
            throw new EncryptionException("Authentication tag is required for AES-GCM decryption");

        try
        {
            var plaintext = new byte[encryptedData.Ciphertext.Length];

#if NETSTANDARD2_0
            // AesGcm is not available in netstandard2.0
            throw new PlatformNotSupportedException(
                "AES-GCM is not available in .NET Standard 2.0. " +
                "Please target .NET 6.0 or later for AES-GCM support, " +
                "or use AesCbcHmacMessageEncryptor for .NET Standard 2.0 compatibility.");
#elif NET8_0_OR_GREATER
            // .NET 8+ supports tag size in constructor
            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(
                    encryptedData.InitializationVector,
                    encryptedData.Ciphertext,
                    encryptedData.AuthenticationTag,
                    plaintext);
            }
#else
            // .NET 6-7 don't support tag size parameter
            using (var aes = new AesGcm(_key))
            {
                aes.Decrypt(
                    encryptedData.InitializationVector,
                    encryptedData.Ciphertext,
                    encryptedData.AuthenticationTag,
                    plaintext);
            }
#endif

            return Task.FromResult(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Failed to decrypt message. The message may have been tampered with.", ex);
        }
    }
}
