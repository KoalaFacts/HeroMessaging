using System.Security.Cryptography;
using HeroMessaging.Abstractions.Security;
using HeroMessaging.Security.Signing;
using Xunit;

namespace HeroMessaging.Security.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class HmacSha256MessageSignerSpanTests
{
    [Fact]
    public void Sign_WithValidData_ReturnsExpectedLength()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test message");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.Equal(32, bytesWritten); // SHA256 produces 32 bytes
        Assert.NotEqual(new byte[signature.Length], signature); // Should be filled
    }

    [Fact]
    public void Sign_WithInsufficientBuffer_ThrowsArgumentException()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = new byte[signer.SignatureSize - 1]; // Too small
        var context = new SecurityContext(TimeProvider.System);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => signer.Sign(data, signature, context));
        Assert.Contains("Signature buffer must be at least", exception.Message);
    }

    [Fact]
    public void Sign_SameDataTwice_ProducesSameSignature()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Consistent data");
        var signature1 = new byte[signer.SignatureSize];
        var signature2 = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        signer.Sign(data, signature1, context);
        signer.Sign(data, signature2, context);

        // Assert - HMAC should be deterministic
        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_DifferentData_ProducesDifferentSignatures()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data1 = System.Text.Encoding.UTF8.GetBytes("Message 1");
        var data2 = System.Text.Encoding.UTF8.GetBytes("Message 2");
        var signature1 = new byte[signer.SignatureSize];
        var signature2 = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        signer.Sign(data1, signature1, context);
        signer.Sign(data2, signature2, context);

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void TrySign_WithValidBuffer_ReturnsTrue()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test data");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = signer.TrySign(data, signature, context, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(signer.SignatureSize, bytesWritten);
    }

    [Fact]
    public void TrySign_WithInsufficientBuffer_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = new byte[signer.SignatureSize - 1]; // Too small
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success = signer.TrySign(data, signature, context, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Verify_WithValidSignature_ReturnsTrue()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Message to verify");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, signature, context);

        // Act
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_WithTamperedData_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var originalData = System.Text.Encoding.UTF8.GetBytes("Original message");
        var tamperedData = System.Text.Encoding.UTF8.GetBytes("Tampered message");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(originalData, signature, context);

        // Act
        var isValid = signer.Verify(tamperedData, signature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_WithTamperedSignature_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Data");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, signature, context);

        // Tamper with signature
        signature[0] ^= 0xFF;

        // Act
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_WithWrongSignatureLength_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Data");
        var wrongLengthSignature = new byte[signer.SignatureSize - 1];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var isValid = signer.Verify(data, wrongLengthSignature, context);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_WithWrongKey_ReturnsFalse()
    {
        // Arrange
        var signer1 = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var signer2 = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System); // Different key
        var data = System.Text.Encoding.UTF8.GetBytes("Secret data");
        var signature = new byte[signer1.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer1.Sign(data, signature, context);

        // Act
        var isValid = signer2.Verify(data, signature, context);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void SignVerify_WithVariousDataSizes_WorksCorrectly(int size)
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = new byte[size];
        if (size > 0)
        {
            RandomNumberGenerator.Fill(data);
        }
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var signBytes = signer.Sign(data, signature, context);
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, signBytes);
        Assert.True(isValid);
    }

    [Fact]
    public void Sign_WithLargerBuffer_UsesOnlyRequiredSpace()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Data");
        var signature = new byte[signer.SignatureSize + 100]; // Extra space
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        // Only first SignatureSize bytes should be used
        Assert.NotEqual(new byte[signer.SignatureSize], signature.AsSpan(0, signer.SignatureSize).ToArray());
        // Extra bytes should be untouched (still zero)
        Assert.Equal(new byte[100], signature.AsSpan(signer.SignatureSize, 100).ToArray());
    }

    [Fact]
    public void SpanOperations_WithStackAllocation_WorkCorrectly()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var context = new SecurityContext(TimeProvider.System);
        ReadOnlySpan<byte> data = System.Text.Encoding.UTF8.GetBytes("Stack allocated message");

        Span<byte> signature = stackalloc byte[signer.SignatureSize];

        // Act
        var bytesWritten = signer.Sign(data, signature, context);
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_ConstantTimeComparison_AllPositionsDifferent()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test data for timing");
        var validSignature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, validSignature, context);

        // Create signatures that differ at each position
        for (int i = 0; i < signer.SignatureSize; i++)
        {
            var tamperedSignature = new byte[signer.SignatureSize];
            Array.Copy(validSignature, tamperedSignature, tamperedSignature.Length);
            tamperedSignature[i] ^= 0xFF; // Flip bits at position i

            // Act
            var isValid = signer.Verify(data, tamperedSignature, context);

            // Assert - All should be invalid regardless of position
            Assert.False(isValid, $"Signature tampered at position {i} should be invalid");
        }
    }

    [Fact]
    public void Sign_EmptyData_ProducesValidSignature()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var emptyData = Array.Empty<byte>();
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(emptyData, signature, context);
        var isValid = signer.Verify(emptyData, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_EmptyDataWithWrongSignature_ReturnsFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var emptyData = Array.Empty<byte>();
        var someData = System.Text.Encoding.UTF8.GetBytes("Not empty");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(someData, signature, context); // Sign non-empty data

        // Act
        var isValid = signer.Verify(emptyData, signature, context); // Verify empty data

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Sign_WithMinimumKeySize_WorksCorrectly()
    {
        // Arrange - Minimum key size is 16 bytes (128 bits)
        var key = new byte[16];
        RandomNumberGenerator.Fill(key);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.True(isValid);
    }

    [Fact]
    public void Sign_WithMaximumRecommendedKeySize_WorksCorrectly()
    {
        // Arrange - 256 bits is the recommended size
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.True(isValid);
    }

    [Fact]
    public void Sign_WithLargerKey_WorksCorrectly()
    {
        // Arrange - Keys larger than 32 bytes are allowed
        var key = new byte[64];
        RandomNumberGenerator.Fill(key);
        var signer = new HmacSha256MessageSigner(key, TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Test");
        var signature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var bytesWritten = signer.Sign(data, signature, context);
        var isValid = signer.Verify(data, signature, context);

        // Assert
        Assert.Equal(signer.SignatureSize, bytesWritten);
        Assert.True(isValid);
    }

    [Fact]
    public void TrySign_CalledMultipleTimes_ProducesConsistentResults()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Consistent message");
        var signature1 = new byte[signer.SignatureSize];
        var signature2 = new byte[signer.SignatureSize];
        var signature3 = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        // Act
        var success1 = signer.TrySign(data, signature1, context, out var bytes1);
        var success2 = signer.TrySign(data, signature2, context, out var bytes2);
        var success3 = signer.TrySign(data, signature3, context, out var bytes3);

        // Assert
        Assert.True(success1);
        Assert.True(success2);
        Assert.True(success3);
        Assert.Equal(bytes1, bytes2);
        Assert.Equal(bytes2, bytes3);
        Assert.Equal(signature1, signature2);
        Assert.Equal(signature2, signature3);
    }

    [Fact]
    public void Verify_WithSingleBitFlip_InEveryByte_AllReturnFalse()
    {
        // Arrange
        var signer = HmacSha256MessageSigner.CreateWithRandomKey(TimeProvider.System);
        var data = System.Text.Encoding.UTF8.GetBytes("Comprehensive test");
        var validSignature = new byte[signer.SignatureSize];
        var context = new SecurityContext(TimeProvider.System);

        signer.Sign(data, validSignature, context);

        // Act & Assert - Test flipping each bit in each byte
        for (int byteIndex = 0; byteIndex < signer.SignatureSize; byteIndex++)
        {
            for (int bitIndex = 0; bitIndex < 8; bitIndex++)
            {
                var tamperedSignature = new byte[signer.SignatureSize];
                Array.Copy(validSignature, tamperedSignature, tamperedSignature.Length);
                tamperedSignature[byteIndex] ^= (byte)(1 << bitIndex); // Flip single bit

                var isValid = signer.Verify(data, tamperedSignature, context);

                Assert.False(isValid,
                    $"Signature with bit {bitIndex} flipped in byte {byteIndex} should be invalid");
            }
        }
    }
}
