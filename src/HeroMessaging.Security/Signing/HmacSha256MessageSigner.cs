using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;

namespace HeroMessaging.Security.Signing;

/// <summary>
/// Provides HMAC-SHA256 message signing for integrity and authenticity.
/// SECURITY: Implements IDisposable to securely clear key material from memory.
/// </summary>
public sealed class HmacSha256MessageSigner : IMessageSigner, IDisposable
{
    private const int KeySize = 32; // 256 bits recommended for HMAC-SHA256
    private const int HashSize = 32; // SHA256 produces 32 bytes
    private readonly byte[] _key;
    private readonly string? _keyId;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public string Algorithm => "HMAC-SHA256";
    public int SignatureSize => HashSize;

    /// <summary>
    /// Creates a new HMAC-SHA256 signer with the specified key
    /// </summary>
    /// <param name="key">The signing key (recommended: 256 bits)</param>
    /// <param name="timeProvider">Time provider for timestamps</param>
    /// <param name="keyId">Optional key identifier for key rotation</param>
    public HmacSha256MessageSigner(byte[] key, TimeProvider timeProvider, string? keyId = null)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (key.Length < 16)
            throw new ArgumentException("Key must be at least 128 bits (16 bytes)", nameof(key));

        _key = new byte[key.Length];
        Array.Copy(key, _key, key.Length);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _keyId = keyId;
    }

    /// </summary>
    /// <summary>
    /// Creates a new HMAC-SHA256 signer by generating a random key.
    /// SECURITY: The generated key is zeroed from memory after being copied to the signer.
    /// </summary>
    public static HmacSha256MessageSigner CreateWithRandomKey(TimeProvider timeProvider, string? keyId = null)
    {
        if (timeProvider == null) throw new ArgumentNullException(nameof(timeProvider));
        var key = new byte[KeySize];
        try
        {
            RandomNumberGenerator.Fill(key);
            return new HmacSha256MessageSigner(key, timeProvider, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public Task<MessageSignature> SignAsync(
        byte[] data,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        try
        {
            using (var hmac = new HMACSHA256(_key))
            {
                var signatureBytes = hmac.ComputeHash(data);
                var signature = new MessageSignature(
                    signatureBytes,
                    Algorithm,
                    _keyId,
                    _timeProvider.GetUtcNow());

                return Task.FromResult(signature);
            }
        }
        catch (CryptographicException ex)
        {
            throw new SecurityException("Failed to sign message", ex);
        }
    }

    public Task<bool> VerifyAsync(
        byte[] data,
        MessageSignature signature,
        SecurityContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (signature == null)
            throw new ArgumentNullException(nameof(signature));

        if (signature.Algorithm != Algorithm)
            throw new SignatureVerificationException($"Unsupported algorithm: {signature.Algorithm}. Expected: {Algorithm}");

        try
        {
            using (var hmac = new HMACSHA256(_key))
            {
                var expectedSignature = hmac.ComputeHash(data);

                // Use constant-time comparison to prevent timing attacks
                var isValid = CryptographicOperations.FixedTimeEquals(expectedSignature, signature.SignatureBytes);

                return Task.FromResult(isValid);
            }
        }
        catch (CryptographicException ex)
        {
            throw new SignatureVerificationException("Failed to verify signature", ex);
        }
    }

    public int Sign(ReadOnlySpan<byte> data, Span<byte> signature, SecurityContext context)
    {
        ThrowIfDisposed();
        if (signature.Length < HashSize)
            throw new ArgumentException($"Signature buffer must be at least {HashSize} bytes", nameof(signature));

        try
        {
            using (var hmac = new HMACSHA256(_key))
            {
                if (!hmac.TryComputeHash(data, signature, out var bytesWritten))
                {
                    throw new SecurityException("Failed to compute HMAC signature");
                }
                return bytesWritten;
            }
        }
        catch (CryptographicException ex)
        {
            throw new SecurityException("Failed to sign message", ex);
        }
    }

    public bool TrySign(ReadOnlySpan<byte> data, Span<byte> signature, SecurityContext context, out int bytesWritten)
    {
        try
        {
            bytesWritten = Sign(data, signature, context);
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, SecurityContext context)
    {
        ThrowIfDisposed();
        if (signature.Length != HashSize)
            return false;

        try
        {
            Span<byte> expectedSignature = stackalloc byte[HashSize];

            using (var hmac = new HMACSHA256(_key))
            {
                if (!hmac.TryComputeHash(data, expectedSignature, out _))
                {
                    return false;
                }
            }

            // Use built-in constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(expectedSignature, signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Securely clears the signing key from memory
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
