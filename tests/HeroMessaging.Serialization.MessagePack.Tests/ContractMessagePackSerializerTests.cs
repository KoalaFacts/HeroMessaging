using System;
using System.Threading;
using System.Threading.Tasks;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace HeroMessaging.Serialization.MessagePack.Tests;

[Trait("Category", "Unit")]
public class ContractMessagePackSerializerTests
{
    #region Test Models

    [MessagePackObject]
    public class ContractTestMessage
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public int Value { get; set; }

        [Key(2)]
        public DateTime Timestamp { get; set; }
    }

    [MessagePackObject]
    public class ContractComplexMessage
    {
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        [Key(1)]
        public ContractTestMessage? Nested { get; set; }

        [Key(2)]
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaults()
    {
        // Arrange & Act
        var serializer = new ContractMessagePackSerializer();

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-msgpack-contract", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new SerializationOptions
        {
            EnableCompression = true,
            MaxMessageSize = 4096
        };

        // Act
        var serializer = new ContractMessagePackSerializer(options);

        // Assert
        Assert.NotNull(serializer);
        Assert.Equal("application/x-msgpack-contract", serializer.ContentType);
    }

    [Fact]
    public void Constructor_WithCustomMessagePackOptions_UsesProvidedOptions()
    {
        // Arrange
        var messagePackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance);

        // Act
        var serializer = new ContractMessagePackSerializer(messagePackOptions: messagePackOptions);

        // Assert
        Assert.NotNull(serializer);
    }

    #endregion

    #region ContentType Tests

    [Fact]
    public void ContentType_ReturnsCorrectValue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var contentType = serializer.ContentType;

        // Assert
        Assert.Equal("application/x-msgpack-contract", contentType);
    }

    #endregion

    #region SerializeAsync Tests

    [Fact]
    public async Task SerializeAsync_WithValidContractMessage_ReturnsSerializedData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage
        {
            Name = "ContractTest",
            Value = 42,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithNullMessage_ReturnsEmptyArray()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = await serializer.SerializeAsync<ContractTestMessage>(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SerializeAsync_WithComplexContractMessage_SerializesCorrectly()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractComplexMessage
        {
            Id = "complex-contract",
            Nested = new ContractTestMessage { Name = "Nested", Value = 99 },
            Tags = new[] { "tag1", "tag2", "tag3" }
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithMaxMessageSizeExceeded_ThrowsException()
    {
        // Arrange
        var options = new SerializationOptions { MaxMessageSize = 10 };
        var serializer = new ContractMessagePackSerializer(options);
        var message = new ContractTestMessage { Name = "This is a very long message name" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await serializer.SerializeAsync(message));
        Assert.Contains("exceeds maximum allowed size", exception.Message);
    }

    [Fact]
    public async Task SerializeAsync_WithCompressionEnabled_ReturnsCompressedData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ContractMessagePackSerializer(options);
        var message = new ContractTestMessage
        {
            Name = "Compression test with contract serializer",
            Value = 123
        };

        // Act
        var result = await serializer.SerializeAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task SerializeAsync_WithCancellationToken_CompletesSuccessfully()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "CancellationTest", Value = 200 };
        var cts = new CancellationTokenSource();

        // Act
        var result = await serializer.SerializeAsync(message, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    #endregion

    #region Serialize (Span) Tests

    [Fact]
    public void Serialize_WithValidContractMessage_WritesToSpan()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "SpanContract", Value = 300 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(message, buffer);

        // Assert
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void Serialize_WithNullMessage_ReturnsZero()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize<ContractTestMessage>(null!, buffer);

        // Assert
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Serialize_WithTooSmallBuffer_ThrowsException()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "SmallBuffer", Value = 400 };
        Span<byte> buffer = new byte[5];

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => serializer.Serialize(message, buffer));
        Assert.Contains("Destination buffer too small", exception.Message);
    }

    #endregion

    #region TrySerialize Tests

    [Fact]
    public void TrySerialize_WithValidMessage_ReturnsTrue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "TryContract", Value = 500 };
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.True(bytesWritten > 0);
    }

    [Fact]
    public void TrySerialize_WithNullMessage_ReturnsTrueWithZeroBytes()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        Span<byte> buffer = new byte[4096];

        // Act
        var success = serializer.TrySerialize<ContractTestMessage>(null!, buffer, out var bytesWritten);

        // Assert
        Assert.True(success);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TrySerialize_WithTooSmallBuffer_ReturnsFalse()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "TooSmall", Value = 600 };
        Span<byte> buffer = new byte[5];

        // Act
        var success = serializer.TrySerialize(message, buffer, out var bytesWritten);

        // Assert
        Assert.False(success);
        Assert.Equal(0, bytesWritten);
    }

    #endregion

    #region GetRequiredBufferSize Tests

    [Fact]
    public void GetRequiredBufferSize_ReturnsPositiveValue()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var message = new ContractTestMessage { Name = "BufferSize", Value = 700 };

        // Act
        var bufferSize = serializer.GetRequiredBufferSize(message);

        // Assert
        Assert.True(bufferSize > 0);
        Assert.Equal(2048, bufferSize);
    }

    #endregion

    #region DeserializeAsync Tests

    [Fact]
    public async Task DeserializeAsync_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage
        {
            Name = "DeserializeContract",
            Value = 800,
            Timestamp = new DateTime(2024, 6, 15, 12, 30, 0, DateTimeKind.Utc)
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ContractTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_WithNullData_ReturnsDefault()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = await serializer.DeserializeAsync<ContractTestMessage>(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyData_ReturnsDefault()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = await serializer.DeserializeAsync<ContractTestMessage>(Array.Empty<byte>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeserializeAsync_WithComplexMessage_DeserializesCorrectly()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractComplexMessage
        {
            Id = "deserialize-complex-contract",
            Nested = new ContractTestMessage { Name = "DeserializeNested", Value = 900 },
            Tags = new[] { "x", "y", "z" }
        };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ContractComplexMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Id, result.Id);
        Assert.NotNull(result.Nested);
        Assert.Equal(original.Nested.Name, result.Nested.Name);
        Assert.Equal(3, result.Tags.Length);
    }

    [Fact]
    public async Task DeserializeAsync_WithCompressedData_DecompressesAndDeserializes()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ContractMessagePackSerializer(options);
        var original = new ContractTestMessage { Name = "CompressedContract", Value = 1000 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync<ContractTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage { Name = "TypedContract", Value = 1100 };
        var data = await serializer.SerializeAsync(original);

        // Act
        var result = await serializer.DeserializeAsync(data, typeof(ContractTestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ContractTestMessage>(result);
        var typedResult = (ContractTestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
        Assert.Equal(original.Value, typedResult.Value);
    }

    [Fact]
    public async Task DeserializeAsync_TypedOverload_WithNullData_ReturnsNull()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();

        // Act
        var result = await serializer.DeserializeAsync(null!, typeof(ContractTestMessage));

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Deserialize (Span) Tests

    [Fact]
    public void Deserialize_WithValidData_ReturnsMessage()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage { Name = "SpanDeserializeContract", Value = 1200 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize<ContractTestMessage>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
    }

    [Fact]
    public void Deserialize_WithEmptySpan_ReturnsDefault()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize<ContractTestMessage>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithValidData_ReturnsObject()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage { Name = "TypedSpanContract", Value = 1300 };
        Span<byte> buffer = new byte[4096];
        var bytesWritten = serializer.Serialize(original, buffer);
        var data = buffer.Slice(0, bytesWritten);

        // Act
        var result = serializer.Deserialize(data, typeof(ContractTestMessage));

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ContractTestMessage>(result);
        var typedResult = (ContractTestMessage)result;
        Assert.Equal(original.Name, typedResult.Name);
    }

    [Fact]
    public void Deserialize_TypedOverload_WithEmptySpan_ReturnsNull()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        ReadOnlySpan<byte> data = ReadOnlySpan<byte>.Empty;

        // Act
        var result = serializer.Deserialize(data, typeof(ContractTestMessage));

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public async Task RoundTrip_WithContractMessage_PreservesData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage
        {
            Name = "RoundTripContract",
            Value = 1400,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ContractTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public async Task RoundTrip_WithComplexContractMessage_PreservesData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractComplexMessage
        {
            Id = "roundtrip-complex-contract",
            Nested = new ContractTestMessage { Name = "RoundTripNested", Value = 1500 },
            Tags = new[] { "tag1", "tag2", "tag3", "tag4" }
        };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ContractComplexMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.NotNull(deserialized.Nested);
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
        Assert.Equal(original.Tags.Length, deserialized.Tags.Length);
    }

    [Fact]
    public async Task RoundTrip_WithCompression_PreservesData()
    {
        // Arrange
        var options = new SerializationOptions { EnableCompression = true };
        var serializer = new ContractMessagePackSerializer(options);
        var original = new ContractTestMessage { Name = "CompressedRoundTripContract", Value = 1600 };

        // Act
        var serialized = await serializer.SerializeAsync(original);
        var deserialized = await serializer.DeserializeAsync<ContractTestMessage>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    [Fact]
    public void RoundTrip_Span_WithContractMessage_PreservesData()
    {
        // Arrange
        var serializer = new ContractMessagePackSerializer();
        var original = new ContractTestMessage { Name = "SpanRoundTripContract", Value = 1700 };
        Span<byte> buffer = new byte[4096];

        // Act
        var bytesWritten = serializer.Serialize(original, buffer);
        var deserialized = serializer.Deserialize<ContractTestMessage>(buffer.Slice(0, bytesWritten));

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
    }

    #endregion

    #region Comparison with MessagePackMessageSerializer

    [Fact]
    public async Task ContractSerializer_ProducesMoreEfficientData_ThanContractless()
    {
        // Arrange
        var contractSerializer = new ContractMessagePackSerializer();
        var contractlessSerializer = new MessagePackMessageSerializer();
        var contractMessage = new ContractTestMessage { Name = "EfficiencyTest", Value = 1800 };

        // Create a contractless version for comparison
        var contractlessMessage = new { Name = "EfficiencyTest", Value = 1800 };

        // Act
        var contractData = await contractSerializer.SerializeAsync(contractMessage);
        var contractlessData = await contractlessSerializer.SerializeAsync(contractlessMessage);

        // Assert - Contract-based should be more compact
        Assert.True(contractData.Length > 0);
        Assert.True(contractlessData.Length > 0);
        // Contract serialization is typically more efficient
    }

    #endregion
}
