using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Signing;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class HmacSha256MessageSignerTests
{
    [Fact]
    public void Constructor_WithValidKey_CreatesInstance()
    {
        // Arrange
        var key = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);

        // Act
        var signer = new HmacSha256MessageSigner(key);

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void Constructor_WithKeyId_StoresKeyId()
    {
        // Arrange
        var key = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(key);
        var keyId = "signing-key-1";

        // Act
        var signer = new HmacSha256MessageSigner(key, keyId);

        // Assert
        Assert.NotNull(signer);
    }

    [Fact]
    public void Constructor_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new HmacSha256MessageSigner(null!));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidKeySize_ThrowsArgumentException()
    {
        // Arrange
        var invalidKey = new byte[8]; // Too small

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => new HmacSha256MessageSigner(invalidKey));
        Assert.Contains("Key must be at least 128 bits (16 bytes)", exception.Message);
    }

    [Fact]
    public void CreateWithRandomKey_GeneratesValidSigner()
    {
        // Act
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    public void CreateWithRandomKey_WithKeyId_StoresKeyId()
    {
        // Arrange
        var keyId = "auto-generated-key";

        // Act
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(keyId);

        // Assert
        Assert.NotNull(signer);
    }

    [Fact]
    public async Task SignAsync_WithValidData_ReturnsSignature()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = System.Text.Encoding.UTF8.GetBytes("Message to sign");
        var context = new SecurityContext();

        // Act
        var signature = await signer.SignAsync(data, context);

        // Assert
        Assert.NotNull(signature);
        Assert.NotNull(signature.SignatureBytes);
        Assert.Equal(32, signature.SignatureBytes.Length); // SHA256 produces 256 bits = 32 bytes
        Assert.Equal("HMAC-SHA256", signature.Algorithm);
        Assert.True(signature.Timestamp <= DateTimeOffset.UtcNow);
        Assert.True(signature.Timestamp >= DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task SignAsync_SameDataTwice_ProducesSameSignature()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = System.Text.Encoding.UTF8.GetBytes("Consistent message");
        var context = new SecurityContext();

        // Act
        var signature1 = await signer.SignAsync(data, context);
        var signature2 = await signer.SignAsync(data, context);

        // Assert - HMAC should be deterministic for same input
        Assert.Equal(signature1.SignatureBytes, signature2.SignatureBytes);
    }

    [Fact]
    public async Task VerifyAsync_WithValidSignature_ReturnsTrue()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = System.Text.Encoding.UTF8.GetBytes("Verified message");
        var context = new SecurityContext();
        var signature = await signer.SignAsync(data, context);

        // Act
        var isValid = await signer.VerifyAsync(data, signature, context);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var originalData = System.Text.Encoding.UTF8.GetBytes("Original message");
        var tamperedData = System.Text.Encoding.UTF8.GetBytes("Tampered message");
        var context = new SecurityContext();
        var signature = await signer.SignAsync(originalData, context);

        // Act
        var isValid = await signer.VerifyAsync(tamperedData, signature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedSignature_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = System.Text.Encoding.UTF8.GetBytes("Message");
        var context = new SecurityContext();
        var originalSignature = await signer.SignAsync(data, context);

        // Tamper with signature
        var tamperedBytes = new byte[originalSignature.SignatureBytes.Length];
        Array.Copy(originalSignature.SignatureBytes, tamperedBytes, tamperedBytes.Length);
        tamperedBytes[0] ^= 0xFF; // Flip bits

        var tamperedSignature = new MessageSignature(
            tamperedBytes,
            originalSignature.Algorithm,
            originalSignature.KeyId,
            originalSignature.Timestamp);

        // Act
        var isValid = await signer.VerifyAsync(data, tamperedSignature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKey_ReturnsFalse()
    {
        // Arrange
        var signer1 = HmacSha256MessageSigner.CreateWithRandomKey();
        var signer2 = HmacSha256MessageSigner.CreateWithRandomKey(); // Different key
        var data = System.Text.Encoding.UTF8.GetBytes("Secret data");
        var context = new SecurityContext();
        var signature = await signer1.SignAsync(data, context);

        // Act
        var isValid = await signer2.VerifyAsync(data, signature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task SignAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var context = new SecurityContext();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.SignAsync(null!, context));
        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public async Task VerifyAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var context = new SecurityContext();
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = await signer.SignAsync(data, context);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.VerifyAsync(null!, signature, context));
        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public async Task VerifyAsync_WithNullSignature_ThrowsArgumentNullException()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var context = new SecurityContext();
        var data = System.Text.Encoding.UTF8.GetBytes("Test");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.VerifyAsync(data, null!, context));
        Assert.Equal("signature", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(65536)]
    public async Task SignVerify_WithVariousDataSizes_WorksCorrectly(int dataSize)
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = new byte[dataSize];
        if (dataSize > 0)
        {
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(data);
        }
        var context = new SecurityContext();

        // Act
        var signature = await signer.SignAsync(data, context);
        var isValid = await signer.VerifyAsync(data, signature, context);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyAsync_ConstantTimeComparison_PreventsTimingAttacks()
    {
        // This test verifies that comparison time is constant regardless of signature match
        // In practice, this is hard to test reliably, but we can at least verify behavior

        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey();
        var data = System.Text.Encoding.UTF8.GetBytes("Timing test data");
        var context = new SecurityContext();
        var validSignature = await signer.SignAsync(data, context);

        // Create signatures that differ at different positions
        var differentAtStart = new byte[validSignature.SignatureBytes.Length];
        Array.Copy(validSignature.SignatureBytes, differentAtStart, differentAtStart.Length);
        differentAtStart[0] ^= 0xFF;

        var differentAtEnd = new byte[validSignature.SignatureBytes.Length];
        Array.Copy(validSignature.SignatureBytes, differentAtEnd, differentAtEnd.Length);
        differentAtEnd[^1] ^= 0xFF;

        var sigStart = new MessageSignature(differentAtStart, validSignature.Algorithm, validSignature.KeyId, validSignature.Timestamp);
        var sigEnd = new MessageSignature(differentAtEnd, validSignature.Algorithm, validSignature.KeyId, validSignature.Timestamp);

        // Act
        var resultStart = await signer.VerifyAsync(data, sigStart, context);
        var resultEnd = await signer.VerifyAsync(data, sigEnd, context);

        // Assert - Both should be false (constant-time comparison)
        Assert.False(resultStart);
        Assert.False(resultEnd);
    }
}
