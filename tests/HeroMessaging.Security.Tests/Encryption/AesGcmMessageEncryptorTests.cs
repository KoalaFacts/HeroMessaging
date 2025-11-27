using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Encryption;
using Xunit;

namespace HeroMessaging.Security.Tests.Encryption;

/// <summary>
/// Unit tests for AesGcmMessageEncryptor
/// </summary>
public sealed class AesGcmMessageEncryptorTests
{
    private static byte[] GenerateRandomKey(int size = 32)
    {
        var key = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return key;
    }

    private static byte[] GenerateRandomData(int size = 100)
    {
        var data = new byte[size];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(data);
        }
        return data;
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidKey_Succeeds()
    {
        // Arrange
        var key = GenerateRandomKey();
        var keyId = "test-key-1";

        // Act
        var encryptor = new AesGcmMessageEncryptor(key, keyId);

        // Assert
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
        Assert.Equal(12, encryptor.IVSize);
        Assert.Equal(16, encryptor.TagSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new AesGcmMessageEncryptor(null!));
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithIncorrectKeySize_ThrowsArgumentException()
    {
        // Arrange
        var shortKey = new byte[16]; // 128 bits instead of 256
        var longKey = new byte[64]; // 512 bits instead of 256

        // Act & Assert
        var exShort = Assert.Throws<ArgumentException>(() => new AesGcmMessageEncryptor(shortKey));
        Assert.Contains("Key must be 32 bytes", exShort.Message);
        Assert.Equal("key", exShort.ParamName);

        var exLong = Assert.Throws<ArgumentException>(() => new AesGcmMessageEncryptor(longKey));
        Assert.Contains("Key must be 32 bytes", exLong.Message);
        Assert.Equal("key", exLong.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateWithRandomKey_Succeeds()
    {
        // Act
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();

        // Assert
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateWithRandomKey_WithKeyId_Succeeds()
    {
        // Act
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey("key-123");

        // Assert
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    #endregion

    #region Async Encryption/Decryption Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EncryptAsync_WithValidData_ReturnsEncryptedData()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key, "test-key");
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var result = await encryptor.EncryptAsync(plaintext, context);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Ciphertext);
        Assert.NotNull(result.InitializationVector);
        Assert.NotNull(result.AuthenticationTag);
        Assert.Equal(plaintext.Length, result.Ciphertext.Length);
        Assert.Equal(12, result.InitializationVector.Length);
        Assert.Equal(16, result.AuthenticationTag.Length);
        Assert.Equal("AES-256-GCM", result.Algorithm);
        Assert.Equal("test-key", result.KeyId);
        Assert.NotEqual(plaintext, result.Ciphertext); // Should be different
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EncryptAsync_WithNullPlaintext_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => encryptor.EncryptAsync(null!, context));
        Assert.Equal("plaintext", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EncryptAsync_WithEmptyPlaintext_ReturnsEmptyCiphertext()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = Array.Empty<byte>();
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var result = await encryptor.EncryptAsync(plaintext, context);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Ciphertext);
        Assert.NotNull(result.AuthenticationTag);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithValidEncryptedData_ReturnsPlaintext()
    {
        // Arrange
        var key = GenerateRandomKey();
        var originalPlaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var encrypted = await encryptor.EncryptAsync(originalPlaintext, context);
        var decrypted = await encryptor.DecryptAsync(encrypted, context);

        // Assert
        Assert.Equal(originalPlaintext, decrypted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithNullEncryptedData_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => encryptor.DecryptAsync(null!, context));
        Assert.Equal("encryptedData", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithWrongAlgorithm_ThrowsEncryptionException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);
        var encryptedData = new EncryptedData(
            new byte[16],
            new byte[12],
            "AES-128-CBC", // Wrong algorithm
            new byte[16]
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(encryptedData, context));
        Assert.Contains("Unsupported algorithm", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithNullAuthenticationTag_ThrowsEncryptionException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);
        var encryptedData = new EncryptedData(
            new byte[16],
            new byte[12],
            "AES-256-GCM",
            null // No authentication tag
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(encryptedData, context));
        Assert.Contains("Authentication tag is required", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithTamperedCiphertext_ThrowsEncryptionException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var encrypted = await encryptor.EncryptAsync(plaintext, context);

        // Tamper with ciphertext
        encrypted.Ciphertext[0] ^= 0xFF;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(encrypted, context));
        Assert.Contains("tampered", ex.Message.ToLower());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DecryptAsync_WithTamperedAuthenticationTag_ThrowsEncryptionException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var encrypted = await encryptor.EncryptAsync(plaintext, context);

        // Tamper with authentication tag
        encrypted.AuthenticationTag![0] ^= 0xFF;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(encrypted, context));
        Assert.Contains("tampered", ex.Message.ToLower());
    }

    #endregion

    #region Span-based Synchronous Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Encrypt_WithValidSpans_ReturnsEncryptedLength()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Assert
        Assert.Equal(plaintext.Length, bytesWritten);
        Assert.NotEqual(plaintext, ciphertext);
        Assert.NotEqual(default, iv[0]); // IV should be populated with random data
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Encrypt_WithInsufficientCiphertextBuffer_ThrowsArgumentException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[50]; // Too small
        var iv = new byte[12];
        var tag = new byte[16];
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Equal("ciphertext", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Encrypt_WithInsufficientIvBuffer_ThrowsArgumentException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[8]; // Too small (need 12)
        var tag = new byte[16];
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Equal("iv", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Encrypt_WithInsufficientTagBuffer_ThrowsArgumentException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[8]; // Too small (need 16)
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Equal("tag", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Decrypt_WithValidSpans_ReturnsDecryptedLength()
    {
        // Arrange
        var key = GenerateRandomKey();
        var originalPlaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Encrypt first
        var ciphertext = new byte[originalPlaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor.Encrypt(originalPlaintext, ciphertext, iv, tag, context);

        // Act
        var plaintext = new byte[originalPlaintext.Length];
        var bytesWritten = encryptor.Decrypt(ciphertext, iv, tag, plaintext, context);

        // Assert
        Assert.Equal(originalPlaintext.Length, bytesWritten);
        Assert.Equal(originalPlaintext, plaintext);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Decrypt_WithInsufficientPlaintextBuffer_ThrowsArgumentException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        var tooSmallBuffer = new byte[50];

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, tooSmallBuffer, context));
        Assert.Equal("plaintext", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Decrypt_WithTamperedData_ThrowsEncryptionException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Tamper with ciphertext
        ciphertext[0] ^= 0xFF;

        var decryptedBuffer = new byte[plaintext.Length];

        // Act & Assert
        var ex = Assert.Throws<EncryptionException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, decryptedBuffer, context));
        Assert.Contains("tampered", ex.Message.ToLower());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryEncrypt_WithValidSpans_Succeeds()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = encryptor.TryEncrypt(plaintext, ciphertext, iv, tag, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(plaintext.Length, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryEncrypt_WithInvalidBuffers_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var ciphertext = new byte[50]; // Too small
        var iv = new byte[12];
        var tag = new byte[16];
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = encryptor.TryEncrypt(plaintext, ciphertext, iv, tag, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryDecrypt_WithValidSpans_Succeeds()
    {
        // Arrange
        var key = GenerateRandomKey();
        var originalPlaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var ciphertext = new byte[originalPlaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor.Encrypt(originalPlaintext, ciphertext, iv, tag, context);

        var plaintext = new byte[originalPlaintext.Length];

        // Act
        var success = encryptor.TryDecrypt(ciphertext, iv, tag, plaintext, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(originalPlaintext.Length, bytesWritten);
        Assert.Equal(originalPlaintext, plaintext);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryDecrypt_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        ciphertext[0] ^= 0xFF; // Tamper
        var decryptedBuffer = new byte[plaintext.Length];

        // Act
        var success = encryptor.TryDecrypt(ciphertext, iv, tag, decryptedBuffer, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region Edge Cases and Security Tests

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task EncryptDecryptAsync_WithVariousDataSizes_PreservesData(int dataSize)
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(dataSize);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var encrypted = await encryptor.EncryptAsync(plaintext, context);
        var decrypted = await encryptor.DecryptAsync(encrypted, context);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EncryptAsync_ProducesUniqueIV_OnMultipleEncryptions()
    {
        // Arrange
        var key = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);
        var encryptor = new AesGcmMessageEncryptor(key);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var result1 = await encryptor.EncryptAsync(plaintext, context);
        var result2 = await encryptor.EncryptAsync(plaintext, context);

        // Assert
        // IV should be different for each encryption (random nonce)
        Assert.NotEqual(result1.InitializationVector, result2.InitializationVector);
        // But both should decrypt to the same plaintext
        var decrypted1 = await encryptor.DecryptAsync(result1, context);
        var decrypted2 = await encryptor.DecryptAsync(result2, context);
        Assert.Equal(decrypted1, decrypted2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Encrypt_WithDifferentKey_CannotDecrypt()
    {
        // Arrange
        var key1 = GenerateRandomKey();
        var key2 = GenerateRandomKey();
        var plaintext = GenerateRandomData(100);

        var encryptor1 = new AesGcmMessageEncryptor(key1);
        var encryptor2 = new AesGcmMessageEncryptor(key2);
        var context = new SecurityContext(TimeProvider.System);

        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[12];
        var tag = new byte[16];
        encryptor1.Encrypt(plaintext, ciphertext, iv, tag, context);

        var decryptedBuffer = new byte[plaintext.Length];

        // Act & Assert
        var ex = Assert.Throws<EncryptionException>(
            () => encryptor2.Decrypt(ciphertext, iv, tag, decryptedBuffer, context));
        Assert.Contains("tampered", ex.Message.ToLower());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Algorithm_PropertyReturnsCorrectValue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);

        // Act
        var algorithm = encryptor.Algorithm;

        // Assert
        Assert.Equal("AES-256-GCM", algorithm);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IVSize_PropertyReturnsCorrectValue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);

        // Act
        var ivSize = encryptor.IVSize;

        // Assert
        Assert.Equal(12, ivSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TagSize_PropertyReturnsCorrectValue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var encryptor = new AesGcmMessageEncryptor(key);

        // Act
        var tagSize = encryptor.TagSize;

        // Assert
        Assert.Equal(16, tagSize);
    }

    #endregion
}
