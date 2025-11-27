using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Encryption;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AesGcmMessageEncryptorSpanTests
{
#if !NETSTANDARD2_0
    [Fact]
    public void Encrypt_WithValidData_ReturnsExpectedLength()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Assert
        Assert.Equal(plaintext.Length, bytesWritten);
        Assert.NotEqual(new byte[iv.Length], iv); // IV should be filled
        Assert.NotEqual(new byte[tag.Length], tag); // Tag should be filled
    }

    [Fact]
    public void Encrypt_WithInsufficientCiphertextBuffer_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var ciphertext = new byte[plaintext.Length - 1]; // Too small
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Contains("Ciphertext buffer must be at least as large as plaintext", exception.Message);
    }

    [Fact]
    public void Encrypt_WithInsufficientIVBuffer_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize - 1]; // Too small
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Contains("IV buffer must be at least", exception.Message);
    }

    [Fact]
    public void Encrypt_WithInsufficientTagBuffer_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize - 1]; // Too small
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Encrypt(plaintext, ciphertext, iv, tag, context));
        Assert.Contains("Tag buffer must be at least", exception.Message);
    }

    [Fact]
    public void TryEncrypt_WithValidBuffers_ReturnsTrue()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = encryptor.TryEncrypt(plaintext, ciphertext, iv, tag, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(plaintext.Length, bytesWritten);
    }

    [Fact]
    public void TryEncrypt_WithInsufficientBuffer_ReturnsFalse()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var ciphertext = new byte[plaintext.Length - 1]; // Too small
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = encryptor.TryEncrypt(plaintext, ciphertext, iv, tag, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Decrypt_WithValidEncryptedData_ReturnsOriginalPlaintext()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var originalPlaintext = System.Text.Encoding.UTF8.GetBytes("Secret message");
        var ciphertext = new byte[originalPlaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(originalPlaintext, ciphertext, iv, tag, context);

        var decryptedPlaintext = new byte[ciphertext.Length];

        // Act
        var bytesWritten = encryptor.Decrypt(ciphertext, iv, tag, decryptedPlaintext, context);

        // Assert
        Assert.Equal(ciphertext.Length, bytesWritten);
        Assert.Equal(originalPlaintext, decryptedPlaintext);
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Original");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Tamper with ciphertext
        ciphertext[0] ^= 0xFF;

        var decrypted = new byte[ciphertext.Length];

        // Act & Assert
        var exception = Assert.Throws<EncryptionException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, decrypted, context));
        Assert.Contains("tampered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decrypt_WithTamperedTag_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Tamper with tag
        tag[0] ^= 0xFF;

        var decrypted = new byte[ciphertext.Length];

        // Act & Assert
        Assert.Throws<EncryptionException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, decrypted, context));
    }

    [Fact]
    public void Decrypt_WithTamperedIV_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Data");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Tamper with IV
        iv[0] ^= 0xFF;

        var decrypted = new byte[ciphertext.Length];

        // Act & Assert
        Assert.Throws<EncryptionException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, decrypted, context));
    }

    [Fact]
    public void Decrypt_WithInsufficientPlaintextBuffer_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        var decrypted = new byte[ciphertext.Length - 1]; // Too small

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, decrypted, context));
        Assert.Contains("Plaintext buffer must be at least as large as ciphertext", exception.Message);
    }

    [Fact]
    public void Decrypt_WithInsufficientIV_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var ciphertext = new byte[16];
        var iv = new byte[encryptor.IVSize - 1]; // Too small
        var tag = new byte[encryptor.TagSize];
        var plaintext = new byte[16];
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, plaintext, context));
        Assert.Contains("IV must be at least", exception.Message);
    }

    [Fact]
    public void Decrypt_WithInsufficientTag_ThrowsArgumentException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var ciphertext = new byte[16];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize - 1]; // Too small
        var plaintext = new byte[16];
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => encryptor.Decrypt(ciphertext, iv, tag, plaintext, context));
        Assert.Contains("Tag must be at least", exception.Message);
    }

    [Fact]
    public void TryDecrypt_WithValidData_ReturnsTrue()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        var decrypted = new byte[ciphertext.Length];

        // Act
        var success = encryptor.TryDecrypt(ciphertext, iv, tag, decrypted, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(plaintext.Length, bytesWritten);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void TryDecrypt_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Tamper
        ciphertext[0] ^= 0xFF;

        var decrypted = new byte[ciphertext.Length];

        // Act
        var success = encryptor.TryDecrypt(ciphertext, iv, tag, decrypted, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecrypt_WithInsufficientBuffer_ReturnsFalse()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Message");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        var decrypted = new byte[ciphertext.Length - 1]; // Too small

        // Act
        var success = encryptor.TryDecrypt(ciphertext, iv, tag, decrypted, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void EncryptDecrypt_WithVariousSizes_WorksCorrectly(int size)
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = new byte[size];
        if (size > 0)
        {
            RandomNumberGenerator.Fill(plaintext);
        }
        var ciphertext = new byte[size];
        var iv = new byte[encryptor.IVSize];
        var tag = new byte[encryptor.TagSize];
        var decrypted = new byte[size];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var encBytes = encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);
        var decBytes = encryptor.Decrypt(ciphertext, iv, tag, decrypted, context);

        // Assert
        Assert.Equal(size, encBytes);
        Assert.Equal(size, decBytes);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentIVs()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Same message");
        var ciphertext1 = new byte[plaintext.Length];
        var iv1 = new byte[encryptor.IVSize];
        var tag1 = new byte[encryptor.TagSize];
        var ciphertext2 = new byte[plaintext.Length];
        var iv2 = new byte[encryptor.IVSize];
        var tag2 = new byte[encryptor.TagSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        encryptor.Encrypt(plaintext, ciphertext1, iv1, tag1, context);
        encryptor.Encrypt(plaintext, ciphertext2, iv2, tag2, context);

        // Assert - IVs should be different (random nonce)
        Assert.NotEqual(iv1, iv2);
        Assert.NotEqual(ciphertext1, ciphertext2);
        Assert.NotEqual(tag1, tag2);
    }

    [Fact]
    public void Encrypt_WithWrongKey_DecryptFails()
    {
        // Arrange
        var encryptor1 = AesGcmMessageEncryptor.CreateWithRandomKey();
        var encryptor2 = AesGcmMessageEncryptor.CreateWithRandomKey(); // Different key
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var ciphertext = new byte[plaintext.Length];
        var iv = new byte[encryptor1.IVSize];
        var tag = new byte[encryptor1.TagSize];
        var decrypted = new byte[plaintext.Length];
        var context = new SecurityContext(TimeProvider.System);

        encryptor1.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Act & Assert
        Assert.Throws<EncryptionException>(
            () => encryptor2.Decrypt(ciphertext, iv, tag, decrypted, context));
    }

    [Fact]
    public void Encrypt_WithLargerBuffers_UsesOnlyRequiredSpace()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Short");
        var ciphertext = new byte[plaintext.Length + 100]; // Extra space
        var iv = new byte[encryptor.IVSize + 50]; // Extra space
        var tag = new byte[encryptor.TagSize + 20]; // Extra space
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        // Assert
        Assert.Equal(plaintext.Length, bytesWritten);
        // Only first IVSize bytes should be used in IV
        Assert.NotEqual(new byte[encryptor.IVSize], iv.AsSpan(0, encryptor.IVSize).ToArray());
        // Only first TagSize bytes should be used in tag
        Assert.NotEqual(new byte[encryptor.TagSize], tag.AsSpan(0, encryptor.TagSize).ToArray());
    }

    [Fact]
    public void SpanOperations_WithStackAllocation_WorkCorrectly()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var context = new SecurityContext(TimeProvider.System);
        ReadOnlySpan<byte> plaintext = System.Text.Encoding.UTF8.GetBytes("Stack allocated");

        Span<byte> ciphertext = stackalloc byte[plaintext.Length];
        Span<byte> iv = stackalloc byte[encryptor.IVSize];
        Span<byte> tag = stackalloc byte[encryptor.TagSize];

        // Act
        var encBytes = encryptor.Encrypt(plaintext, ciphertext, iv, tag, context);

        Span<byte> decrypted = stackalloc byte[ciphertext.Length];
        var decBytes = encryptor.Decrypt(ciphertext, iv, tag, decrypted, context);

        // Assert
        Assert.Equal(plaintext.Length, encBytes);
        Assert.Equal(plaintext.Length, decBytes);
        Assert.True(plaintext.SequenceEqual(decrypted));
    }
#endif
}
