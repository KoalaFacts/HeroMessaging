using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Encryption;

/// <summary>
/// Provides AES-256-GCM authenticated encryption for messages.
/// SECURITY: Implements IDisposable to securely clear key material from memory.
/// </summary>
public sealed class AesGcmMessageEncryptor : IMessageEncryptor, IDisposable
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits (recommended for GCM)
    private const int TagSizeValue = 16; // 128 bits

    private readonly byte[] _key;
    private readonly string? _keyId;
    private bool _disposed;

    public string Algorithm => "AES-256-GCM";
    public int IVSize => NonceSize;
    public int TagSize => TagSizeValue;

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
    /// Creates a new AES-GCM encryptor by generating a random key.
    /// SECURITY: The generated key is zeroed from memory after being copied to the encryptor.
    /// </summary>
    public static AesGcmMessageEncryptor CreateWithRandomKey(string? keyId = null)
    {
        var key = new byte[KeySize];
        try
        {
            RandomNumberGenerator.Fill(key);
            return new AesGcmMessageEncryptor(key, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public Task<EncryptedData> EncryptAsync(
        byte[] plaintext,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
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

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

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
        ThrowIfDisposed();
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (encryptedData.Algorithm != Algorithm)
            throw new EncryptionException($"Unsupported algorithm: {encryptedData.Algorithm}. Expected: {Algorithm}");

        if (encryptedData.AuthenticationTag == null)
            throw new EncryptionException("Authentication tag is required for AES-GCM decryption");

        try
        {
            var plaintext = new byte[encryptedData.Ciphertext.Length];

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(
                    encryptedData.InitializationVector,
                    encryptedData.Ciphertext,
                    encryptedData.AuthenticationTag,
                    plaintext);
            }

            return Task.FromResult(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Failed to decrypt message. The message may have been tampered with.", ex);
        }
    }

    public int Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> iv, Span<byte> tag, SecurityContext context)
    {
        ThrowIfDisposed();
        if (ciphertext.Length < plaintext.Length)
            throw new ArgumentException("Ciphertext buffer must be at least as large as plaintext", nameof(ciphertext));

        if (iv.Length < NonceSize)
            throw new ArgumentException($"IV buffer must be at least {NonceSize} bytes", nameof(iv));

        if (tag.Length < TagSize)
            throw new ArgumentException($"Tag buffer must be at least {TagSize} bytes", nameof(tag));

        try
        {
            // Generate random nonce
            RandomNumberGenerator.Fill(iv[..NonceSize]);

            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Encrypt(iv[..NonceSize], plaintext, ciphertext[..plaintext.Length], tag[..TagSize]);
            }

            return plaintext.Length;
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Failed to encrypt message", ex);
        }
    }

    public bool TryEncrypt(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> iv, Span<byte> tag, SecurityContext context, out int bytesWritten)
    {
        try
        {
            bytesWritten = Encrypt(plaintext, ciphertext, iv, tag, context);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> tag, Span<byte> plaintext, SecurityContext context)
    {
        ThrowIfDisposed();
        if (plaintext.Length < ciphertext.Length)
            throw new ArgumentException("Plaintext buffer must be at least as large as ciphertext", nameof(plaintext));

        if (iv.Length < NonceSize)
            throw new ArgumentException($"IV must be at least {NonceSize} bytes", nameof(iv));

        if (tag.Length < TagSize)
            throw new ArgumentException($"Tag must be at least {TagSize} bytes", nameof(tag));

        try
        {
            using (var aes = new AesGcm(_key, TagSize))
            {
                aes.Decrypt(iv[..NonceSize], ciphertext, tag[..TagSize], plaintext[..ciphertext.Length]);
            }

            return ciphertext.Length;
        }
        catch (CryptographicException ex)
        {
            throw new EncryptionException("Failed to decrypt message. The message may have been tampered with.", ex);
        }
    }

    public bool TryDecrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> tag, Span<byte> plaintext, SecurityContext context, out int bytesWritten)
    {
        try
        {
            bytesWritten = Decrypt(ciphertext, iv, tag, plaintext, context);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    /// <summary>
    /// Securely clears the encryption key from memory
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        // Securely clear the key from memory
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
