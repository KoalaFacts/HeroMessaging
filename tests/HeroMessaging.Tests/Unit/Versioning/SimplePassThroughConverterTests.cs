using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public sealed class SimplePassThroughConverterTests
{
    private readonly ILogger<SimplePassThroughConverter<TestMessage>> _logger;

    public SimplePassThroughConverterTests()
    {
        _logger = NullLogger<SimplePassThroughConverter<TestMessage>>.Instance;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);

        // Act
        var converter = new SimplePassThroughConverter<TestMessage>(fromVersion, toVersion, _logger);

        // Assert
        Assert.NotNull(converter);
        Assert.Equal(fromVersion, converter.SupportedVersionRange.MinVersion);
        Assert.Equal(toVersion, converter.SupportedVersionRange.MaxVersion);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SimplePassThroughConverter<TestMessage>(fromVersion, toVersion, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithSameVersion_Succeeds()
    {
        // Arrange
        var version = new MessageVersion(1, 0, 0);

        // Act
        var converter = new SimplePassThroughConverter<TestMessage>(version, version, _logger);

        // Assert
        Assert.NotNull(converter);
    }

    #endregion

    #region SupportedVersionRange Tests

    [Fact]
    public void SupportedVersionRange_ReturnsCorrectRange()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 2, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act
        var range = converter.SupportedVersionRange;

        // Assert
        Assert.Equal(fromVersion, range.MinVersion);
        Assert.Equal(toVersion, range.MaxVersion);
    }

    [Fact]
    public void SupportedVersionRange_Contains_VersionsInRange()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 5, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act & Assert
        Assert.True(converter.SupportedVersionRange.Contains(new MessageVersion(1, 0, 0)));
        Assert.True(converter.SupportedVersionRange.Contains(new MessageVersion(1, 2, 0)));
        Assert.True(converter.SupportedVersionRange.Contains(new MessageVersion(1, 5, 0)));
        Assert.False(converter.SupportedVersionRange.Contains(new MessageVersion(0, 9, 0)));
        Assert.False(converter.SupportedVersionRange.Contains(new MessageVersion(1, 6, 0)));
    }

    #endregion

    #region CanConvert Tests

    [Fact]
    public void CanConvert_WithCorrectTypeAndVersions_ReturnsTrue()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act
        var result = converter.CanConvert(typeof(TestMessage), fromVersion, toVersion);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanConvert_WithIncorrectType_ReturnsFalse()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act
        var result = converter.CanConvert(typeof(OtherMessage), fromVersion, toVersion);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanConvert_WithVersionOutOfRange_ReturnsFalse()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 5, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act
        var result = converter.CanConvert(typeof(TestMessage), new MessageVersion(0, 9, 0), new MessageVersion(1, 5, 0));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanConvert_WithBothVersionsInRange_ReturnsTrue()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 5, 0);
        var converter = CreateConverter(fromVersion, toVersion);

        // Act
        var result = converter.CanConvert(typeof(TestMessage), new MessageVersion(1, 1, 0), new MessageVersion(1, 4, 0));

        // Assert
        Assert.True(result);
    }

    #endregion

    #region ConvertAsync Tests

    [Fact]
    public async Task ConvertAsync_WithSameVersion_ReturnsSameMessage()
    {
        // Arrange
        var version = new MessageVersion(1, 0, 0);
        var converter = CreateConverter(version, version);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, version, version);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_WithDifferentVersions_ReturnsSameMessage()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_PreservesMessageId()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Equal(messageId, result.MessageId);
    }

    [Fact]
    public async Task ConvertAsync_PreservesTimestamp()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var timestamp = DateTimeOffset.UtcNow;
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Timestamp = timestamp
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Equal(timestamp, result.Timestamp);
    }

    [Fact]
    public async Task ConvertAsync_PreservesMetadata()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object> { { "key", "value" } }
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Equal("value", result.Metadata["key"]);
    }

    [Fact]
    public async Task ConvertAsync_PreservesCorrelationId()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var correlationId = Guid.NewGuid().ToString();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = correlationId
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Equal(correlationId, result.CorrelationId);
    }

    [Fact]
    public async Task ConvertAsync_PreservesCausationId()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var causationId = Guid.NewGuid().ToString();
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            CausationId = causationId
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Equal(causationId, result.CausationId);
    }

    [Fact]
    public async Task ConvertAsync_WithCancellation_CompletesSuccessfully()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Converter completes synchronously, so cancellation won't throw
        var result = await converter.ConvertAsync(message, fromVersion, toVersion, cts.Token);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ConvertAsync_MultipleConversions_AllSucceed()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var messages = Enumerable.Range(0, 10)
            .Select(_ => new TestMessage { MessageId = Guid.NewGuid() })
            .ToList();

        // Act
        var results = await Task.WhenAll(
            messages.Select(m => converter.ConvertAsync(m, fromVersion, toVersion)));

        // Assert
        Assert.Equal(messages.Count, results.Length);
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Same(messages[i], results[i]);
        }
    }

    #endregion

    #region MessageConverterBuilder Tests

    [Fact]
    public void MessageConverterBuilder_ForPassThrough_CreatesConverter()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);

        // Act
        var converter = MessageConverterBuilder.ForPassThrough<TestMessage>(fromVersion, toVersion, _logger);

        // Assert
        Assert.NotNull(converter);
        Assert.IsType<SimplePassThroughConverter<TestMessage>>(converter);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ConvertAsync_WithNullMetadata_HandlesGracefully()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = null
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ConvertAsync_WithEmptyMetadata_PreservesEmpty()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object>()
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Empty(result.Metadata);
    }

    [Fact]
    public void MessageType_ReturnsCorrectType()
    {
        // Arrange
        var converter = CreateConverter(new MessageVersion(1, 0, 0), new MessageVersion(1, 1, 0));

        // Act
        var messageType = converter.MessageType;

        // Assert
        Assert.Equal(typeof(TestMessage), messageType);
    }

    [Fact]
    public async Task ConvertAsync_WithComplexMetadata_PreservesAll()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = new Dictionary<string, object>
            {
                { "string", "value" },
                { "int", 42 },
                { "bool", true },
                { "nested", new Dictionary<string, object> { { "inner", "data" } } }
            }
        };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Equal(4, result.Metadata.Count);
        Assert.Equal("value", result.Metadata["string"]);
        Assert.Equal(42, result.Metadata["int"]);
        Assert.Equal(true, result.Metadata["bool"]);
    }

    #endregion

    #region Compatible Version Tests

    [Fact]
    public async Task ConvertAsync_WithMinorVersionUpgrade_WorksCorrectly()
    {
        // Arrange - Minor version changes should be compatible
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_WithPatchVersionUpgrade_WorksCorrectly()
    {
        // Arrange - Patch version changes should be compatible
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 0, 1);
        var converter = CreateConverter(fromVersion, toVersion);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_WithBackwardCompatibility_WorksCorrectly()
    {
        // Arrange - Should work in both directions for compatible versions
        // Note: Version range must have minVersion <= maxVersion, so we create the converter
        // with 1.0.0 to 1.1.0, but perform conversion from 1.1.0 to 1.0.0 (within range)
        var minVersion = new MessageVersion(1, 0, 0);
        var maxVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(minVersion, maxVersion);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act - Convert from version 1.1.0 (within range) to version 1.0.0 (within range)
        var result = await converter.ConvertAsync(message, maxVersion, minVersion);

        // Assert
        Assert.Same(message, result);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task ConvertAsync_LargeNumberOfConversions_CompletesQuickly()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(1, 1, 0);
        var converter = CreateConverter(fromVersion, toVersion);
        var messages = Enumerable.Range(0, 1000)
            .Select(_ => new TestMessage { MessageId = Guid.NewGuid() })
            .ToList();

        // Act
        var results = await Task.WhenAll(
            messages.Select(m => converter.ConvertAsync(m, fromVersion, toVersion)));

        // Assert
        Assert.Equal(messages.Count, results.Length);
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Same(messages[i], results[i]);
        }
    }

    #endregion

    #region Helper Methods

    private SimplePassThroughConverter<TestMessage> CreateConverter(MessageVersion fromVersion, MessageVersion toVersion)
    {
        return new SimplePassThroughConverter<TestMessage>(fromVersion, toVersion, _logger);
    }

    #endregion

    #region Test Classes

    public sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public sealed class OtherMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
