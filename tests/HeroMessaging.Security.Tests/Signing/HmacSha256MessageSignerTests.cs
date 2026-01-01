using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Signing;
using Xunit;

namespace HeroMessaging.Security.Tests.Signing;

/// <summary>
/// Unit tests for HmacSha256MessageSigner
/// </summary>
public sealed class HmacSha256MessageSignerTests
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
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System, keyId);

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
        Assert.Equal(32, signer.SignatureSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new HmacSha256MessageSigner(null!, TimeProvider.System));
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithKeyTooSmall_ThrowsArgumentException()
    {
        // Arrange
        var smallKey = new byte[8]; // Less than 128 bits (16 bytes)

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new HmacSha256MessageSigner(smallKey, TimeProvider.System));
        Assert.Contains("Key must be at least 128 bits", ex.Message);
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithMinimumValidKeySize_Succeeds()
    {
        // Arrange
        var minimumKey = new byte[16]; // Exactly 128 bits
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(minimumKey);
        }

        // Act
        var signer = new HmacSha256MessageSigner(minimumKey, TimeProvider.System);

        // Assert
        Assert.NotNull(signer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new HmacSha256MessageSigner(key, null!));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateWithRandomKey_Succeeds()
    {
        // Act
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
        Assert.Equal(32, signer.SignatureSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateWithRandomKey_WithKeyId_Succeeds()
    {
        // Act
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System, "key-123");

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("HMAC-SHA256", signer.Algorithm);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateWithRandomKey_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => HmacSha256MessageSigner.CreateWithRandomKey(null!));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    #endregion

    #region Async Signing/Verification Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_WithValidData_ReturnsMessageSignature()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System, "test-key");
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(signature);
        Assert.NotNull(signature.SignatureBytes);
        Assert.Equal(32, signature.SignatureBytes.Length);
        Assert.Equal("HMAC-SHA256", signature.Algorithm);
        Assert.Equal("test-key", signature.KeyId);
        Assert.NotEqual(default, signature.Timestamp);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.SignAsync(null!, context, TestContext.Current.CancellationToken));
        Assert.Equal("data", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_WithEmptyData_ReturnsValidSignature()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = Array.Empty<byte>();
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(signature);
        Assert.Equal(32, signature.SignatureBytes.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithValidSignature_ReturnsTrue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);
        var isValid = await signer.VerifyAsync(data, signature, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.VerifyAsync(null!, signature, context, TestContext.Current.CancellationToken));
        Assert.Equal("data", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithNullSignature_ThrowsArgumentNullException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => signer.VerifyAsync(data, null!, context, TestContext.Current.CancellationToken));
        Assert.Equal("signature", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithWrongAlgorithm_ThrowsSignatureVerificationException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);
        var invalidSignature = new MessageSignature(new byte[32], "HMAC-SHA512"); // Wrong algorithm

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SignatureVerificationException>(
            () => signer.VerifyAsync(data, invalidSignature, context, TestContext.Current.CancellationToken));
        Assert.Contains("Unsupported algorithm", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Tamper with data
        data[0] ^= 0xFF;

        // Act
        var isValid = await signer.VerifyAsync(data, signature, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithTamperedSignature_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Tamper with signature
        signature.SignatureBytes[0] ^= 0xFF;

        // Act
        var isValid = await signer.VerifyAsync(data, signature, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region Span-based Synchronous Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Sign_WithValidSpan_ReturnsSignatureLength()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signature = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);

        // Assert
        Assert.Equal(32, bytesWritten);
        Assert.NotEqual(default, signature[0]); // Signature should be populated
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Sign_WithInsufficientBuffer_ThrowsArgumentException()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var tooSmallBuffer = new byte[16];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => signer.Sign(data, tooSmallBuffer, context));
        Assert.Equal("signature", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Sign_WithEmptyData_ReturnsValidSignature()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = ReadOnlySpan<byte>.Empty;
        var signature = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);

        // Assert
        Assert.Equal(32, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Verify_WithValidSignature_ReturnsTrue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signatureBuffer = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        signer.Sign(data, signatureBuffer, context);
        var isValid = signer.Verify(data, signatureBuffer, context);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Verify_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signatureBuffer = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, signatureBuffer, context);

        // Tamper with data
        data[0] ^= 0xFF;

        // Act
        var isValid = signer.Verify(data, signatureBuffer, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Verify_WithTamperedSignature_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signatureBuffer = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, signatureBuffer, context);

        // Tamper with signature
        signatureBuffer[0] ^= 0xFF;

        // Act
        var isValid = signer.Verify(data, signatureBuffer, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Verify_WithWrongSignatureSize_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var wrongSizeSignature = new byte[16];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var isValid = signer.Verify(data, wrongSizeSignature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySign_WithValidSpan_Succeeds()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signature = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = signer.TrySign(data, signature, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(32, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TrySign_WithInsufficientBuffer_ReturnsFalse()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var tooSmallBuffer = new byte[16];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = signer.TrySign(data, tooSmallBuffer, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region Edge Cases and Security Tests

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(10000)]
    public async Task SignVerifyAsync_WithVariousDataSizes_Succeeds(int dataSize)
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(dataSize);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);
        var isValid = await signer.VerifyAsync(data, signature, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_ProducesDeterministicSignature()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature1 = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);
        var signature2 = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Assert
        // HMAC signatures should be deterministic (same data = same signature)
        Assert.Equal(signature1.SignatureBytes, signature2.SignatureBytes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_WithDifferentKey_ProducedDifferentSignature()
    {
        // Arrange
        var key1 = GenerateRandomKey();
        var key2 = GenerateRandomKey();
        var data = GenerateRandomData(100);

        var signer1 = new HmacSha256MessageSigner(key1, TimeProvider.System);
        var signer2 = new HmacSha256MessageSigner(key2, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature1 = await signer1.SignAsync(data, context, TestContext.Current.CancellationToken);
        var signature2 = await signer2.SignAsync(data, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(signature1.SignatureBytes, signature2.SignatureBytes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyAsync_WithDifferentKey_ReturnsFalse()
    {
        // Arrange
        var key1 = GenerateRandomKey();
        var key2 = GenerateRandomKey();
        var data = GenerateRandomData(100);

        var signer1 = new HmacSha256MessageSigner(key1, TimeProvider.System);
        var signer2 = new HmacSha256MessageSigner(key2, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signature = await signer1.SignAsync(data, context, TestContext.Current.CancellationToken);
        var isValid = await signer2.VerifyAsync(data, signature, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Sign_WithLargeData_Succeeds()
    {
        // Arrange
        var key = GenerateRandomKey();
        var largeData = GenerateRandomData(1000000); // 1 MB
        var signature = new byte[32];
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(largeData, signature, context);

        // Assert
        Assert.Equal(32, bytesWritten);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Algorithm_PropertyReturnsCorrectValue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);

        // Act
        var algorithm = signer.Algorithm;

        // Assert
        Assert.Equal("HMAC-SHA256", algorithm);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SignatureSize_PropertyReturnsCorrectValue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);

        // Act
        var signatureSize = signer.SignatureSize;

        // Assert
        Assert.Equal(32, signatureSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Verify_WithLargeSignatureBuffer_ReturnsTrue()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var largerBuffer = new byte[64]; // Larger than needed
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);

        // Sign into first 32 bytes
        signer.Sign(data, largerBuffer.AsSpan(0, 32), context);

        // Act
        var isValid = signer.Verify(data, largerBuffer.AsSpan(0, 32), context);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SignAsync_TimestampIsSetCorrectly()
    {
        // Arrange
        var key = GenerateRandomKey();
        var data = GenerateRandomData(100);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);
        var beforeTime = DateTimeOffset.UtcNow;

        // Act
        var signature = await signer.SignAsync(data, context, TestContext.Current.CancellationToken);
        var afterTime = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotEqual(default, signature.Timestamp);
        Assert.True(signature.Timestamp >= beforeTime && signature.Timestamp <= afterTime);
    }

    #endregion
}
