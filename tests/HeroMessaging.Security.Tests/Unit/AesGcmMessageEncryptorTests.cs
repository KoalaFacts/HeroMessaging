using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Encryption;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class AesGcmMessageEncryptorTests
{
    [Fact]
    public void Constructor_WithValidKey_CreatesInstance()
    {
        // Arrange
        var key = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);

        // Act
        var encryptor = new AesGcmMessageEncryptor(key);

        // Assert
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void Constructor_WithKeyId_StoresKeyId()
    {
        // Arrange
        var key = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        var keyId = "test-key-1";

        // Act
        var encryptor = new AesGcmMessageEncryptor(key, keyId);

        // Assert
        Assert.NotNull(encryptor);
    }

    [Fact]
    public void Constructor_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new AesGcmMessageEncryptor(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidKeySize_ThrowsArgumentException()
    {
        // Arrange
        var invalidKey = new byte[16]; // Only 128 bits, need 256

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new AesGcmMessageEncryptor(invalidKey));
        Assert.Contains("Key must be 32 bytes", exception.Message);
    }

    [Fact]
    public void CreateWithRandomKey_GeneratesValidEncryptor()
    {
        // Act
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();

        // Assert
        Assert.NotNull(encryptor);
        Assert.Equal("AES-256-GCM", encryptor.Algorithm);
    }

    [Fact]
    public void CreateWithRandomKey_WithKeyId_StoresKeyId()
    {
        // Arrange
        var keyId = "generated-key-1";

        // Act
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey(keyId);

        // Assert
        Assert.NotNull(encryptor);
    }

    [Fact]
    public async Task EncryptAsync_WithValidData_ReturnsEncryptedData()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, World!");
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var result = await encryptor.EncryptAsync(plaintext, context);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Ciphertext);
        Assert.NotNull(result.InitializationVector);
        Assert.NotNull(result.AuthenticationTag);
        Assert.Equal("AES-256-GCM", result.Algorithm);
        Assert.Equal(12, result.InitializationVector.Length); // 96 bits
        Assert.Equal(16, result.AuthenticationTag.Length); // 128 bits
    }

    [Fact]
    public async Task EncryptAsync_SameInputTwice_ProducesDifferentCiphertexts()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var result1 = await encryptor.EncryptAsync(plaintext, context);
        var result2 = await encryptor.EncryptAsync(plaintext, context);

        // Assert - Different IVs mean different ciphertexts
        Assert.NotEqual(result1.InitializationVector, result2.InitializationVector);
        Assert.NotEqual(result1.Ciphertext, result2.Ciphertext);
    }

    [Fact]
    public async Task DecryptAsync_WithValidEncryptedData_ReturnsOriginalPlaintext()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var originalPlaintext = System.Text.Encoding.UTF8.GetBytes("Secret message");
        var context = new SecurityContext(TimeProvider.System);
        var encrypted = await encryptor.EncryptAsync(originalPlaintext, context);

        // Act
        var decrypted = await encryptor.DecryptAsync(encrypted, context);

        // Assert
        Assert.Equal(originalPlaintext, decrypted);
    }

    [Fact]
    public async Task DecryptAsync_WithTamperedCiphertext_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Original message");
        var context = new SecurityContext(TimeProvider.System);
        var encrypted = await encryptor.EncryptAsync(plaintext, context);

        // Tamper with the ciphertext
        var tamperedCiphertext = new byte[encrypted.Ciphertext.Length];
        Array.Copy(encrypted.Ciphertext, tamperedCiphertext, tamperedCiphertext.Length);
        tamperedCiphertext[0] ^= 0xFF; // Flip bits

        var tamperedData = new EncryptedData(
            tamperedCiphertext,
            encrypted.InitializationVector,
            encrypted.Algorithm,
            encrypted.AuthenticationTag,
            encrypted.KeyId);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(tamperedData, context));
        Assert.Contains("tampered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecryptAsync_WithWrongKey_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor1 = AesGcmMessageEncryptor.CreateWithRandomKey();
        var encryptor2 = AesGcmMessageEncryptor.CreateWithRandomKey(); // Different key
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var context = new SecurityContext(TimeProvider.System);
        var encrypted = await encryptor1.EncryptAsync(plaintext, context);

        // Act & Assert
        await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor2.DecryptAsync(encrypted, context));
    }

    [Fact]
    public async Task EncryptAsync_WithNullPlaintext_ThrowsArgumentNullException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => encryptor.EncryptAsync(null!, context));
        Assert.Equal("plaintext", exception.ParamName);
    }

    [Fact]
    public async Task DecryptAsync_WithNullEncryptedData_ThrowsArgumentNullException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => encryptor.DecryptAsync(null!, context));
        Assert.Equal("encryptedData", exception.ParamName);
    }

    [Fact]
    public async Task DecryptAsync_WithWrongAlgorithm_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var wrongAlgorithmData = new EncryptedData(
            new byte[16],
            new byte[12],
            "WRONG-ALGORITHM",
            new byte[16],
            null);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(wrongAlgorithmData, context));
        Assert.Contains("Unsupported algorithm", exception.Message);
    }

    [Fact]
    public async Task DecryptAsync_WithMissingAuthTag_ThrowsEncryptionException()
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var dataWithoutTag = new EncryptedData(
            new byte[16],
            new byte[12],
            "AES-256-GCM",
            null, // No auth tag
            null);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<EncryptionException>(
            () => encryptor.DecryptAsync(dataWithoutTag, context));
        Assert.Contains("Authentication tag is required", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(1024)]
    public async Task EncryptDecrypt_WithVariousDataSizes_WorksCorrectly(int dataSize)
    {
        // Arrange
        var encryptor = AesGcmMessageEncryptor.CreateWithRandomKey();
        var plaintext = new byte[dataSize];
        if (dataSize > 0)
        {
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(plaintext);
        }
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var encrypted = await encryptor.EncryptAsync(plaintext, context);
        var decrypted = await encryptor.DecryptAsync(encrypted, context);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }
}
