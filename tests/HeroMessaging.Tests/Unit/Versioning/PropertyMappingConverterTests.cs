using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public sealed class PropertyMappingConverterTests
{
    private readonly ILogger<PropertyMappingConverter<TestMessage>> _logger;

    public PropertyMappingConverterTests()
    {
        _logger = NullLogger<PropertyMappingConverter<TestMessage>>.Instance;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };

        // Act
        var converter = new PropertyMappingConverter<TestMessage>(fromVersion, toVersion, propertyMappings, _logger);

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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new PropertyMappingConverter<TestMessage>(fromVersion, toVersion, propertyMappings, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullPropertyMappings_ThrowsArgumentNullException()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new PropertyMappingConverter<TestMessage>(fromVersion, toVersion, null!, _logger));
        Assert.Equal("propertyMappings", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyPropertyMappings_Succeeds()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string>();

        // Act
        var converter = new PropertyMappingConverter<TestMessage>(fromVersion, toVersion, propertyMappings, _logger);

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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);

        // Act
        var range = converter.SupportedVersionRange;

        // Assert
        Assert.Equal(fromVersion, range.MinVersion);
        Assert.Equal(toVersion, range.MaxVersion);
    }

    #endregion

    #region CanConvert Tests

    [Fact]
    public void CanConvert_WithCorrectTypeAndVersions_ReturnsTrue()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);

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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);

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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);

        // Act
        var result = converter.CanConvert(typeof(TestMessage), new MessageVersion(0, 9, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ConvertAsync Tests

    [Fact]
    public async Task ConvertAsync_WithSameVersion_ReturnsSameMessage()
    {
        // Arrange
        var version = new MessageVersion(1, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(version, version, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, version, version);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_WithDifferentVersions_ReturnsOriginalMessage()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Same(message, result);
    }

    [Fact]
    public async Task ConvertAsync_WithSingleMapping_ProcessesMessage()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldProperty", "NewProperty" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message.MessageId, result.MessageId);
    }

    [Fact]
    public async Task ConvertAsync_WithMultipleMappings_ProcessesMessage()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string>
        {
            { "OldProperty1", "NewProperty1" },
            { "OldProperty2", "NewProperty2" },
            { "OldProperty3", "NewProperty3" }
        };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message.MessageId, result.MessageId);
    }

    [Fact]
    public async Task ConvertAsync_PreservesMessageId()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var messageId = Guid.NewGuid();
        var message = new TestMessage { MessageId = messageId };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.Equal(messageId, result.MessageId);
    }

    [Fact]
    public async Task ConvertAsync_PreservesMetadata()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Converter completes synchronously, so cancellation won't throw
        var result = await converter.ConvertAsync(message, fromVersion, toVersion, cts.Token);
        Assert.NotNull(result);
    }

    #endregion

    #region MessageConverterBuilder Tests

    [Fact]
    public void MessageConverterBuilder_ForPropertyMapping_CreatesConverter()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };

        // Act
        var converter = MessageConverterBuilder.ForPropertyMapping<TestMessage>(fromVersion, toVersion, propertyMappings, _logger);

        // Assert
        Assert.NotNull(converter);
        Assert.IsType<PropertyMappingConverter<TestMessage>>(converter);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ConvertAsync_WithNullMetadata_HandlesGracefully()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
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
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage
        {
            MessageId = Guid.NewGuid(),
            Metadata = []
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
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0), propertyMappings);

        // Act
        var messageType = converter.MessageType;

        // Assert
        Assert.Equal(typeof(TestMessage), messageType);
    }

    [Fact]
    public async Task ConvertAsync_WithEmptyMappings_ProcessesMessage()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string>();
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(message.MessageId, result.MessageId);
    }

    [Fact]
    public async Task ConvertAsync_WithChainedMappings_ProcessesMessage()
    {
        // Arrange - Simulating property name changes across multiple versions
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(3, 0, 0);
        var propertyMappings = new Dictionary<string, string>
        {
            { "OriginalName", "IntermediateName" },
            { "IntermediateName", "FinalName" }
        };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ConvertAsync_MultipleConversions_AllSucceed()
    {
        // Arrange
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldName", "NewName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
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
            Assert.Equal(messages[i].MessageId, results[i].MessageId);
        }
    }

    #endregion

    #region Bidirectional Mapping Tests

    [Fact]
    public async Task ConvertAsync_ForwardMapping_ProcessesMessage()
    {
        // Arrange - Converting from old to new
        var fromVersion = new MessageVersion(1, 0, 0);
        var toVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "OldPropertyName", "NewPropertyName" } };
        var converter = CreateConverter(fromVersion, toVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act
        var result = await converter.ConvertAsync(message, fromVersion, toVersion);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ConvertAsync_BackwardMapping_ProcessesMessage()
    {
        // Arrange - Converting from new to old (requires inverse mapping)
        // Note: Version range must have minVersion <= maxVersion, so for backward compatibility
        // the converter is created with 1.0.0 to 2.0.0, but conversion can happen in either direction
        var minVersion = new MessageVersion(1, 0, 0);
        var maxVersion = new MessageVersion(2, 0, 0);
        var propertyMappings = new Dictionary<string, string> { { "NewPropertyName", "OldPropertyName" } };
        var converter = CreateConverter(minVersion, maxVersion, propertyMappings);
        var message = new TestMessage { MessageId = Guid.NewGuid() };

        // Act - Convert from version 2.0.0 (within range) to version 1.0.0 (within range)
        var result = await converter.ConvertAsync(message, maxVersion, minVersion);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Helper Methods

    private PropertyMappingConverter<TestMessage> CreateConverter(MessageVersion fromVersion, MessageVersion toVersion, IReadOnlyDictionary<string, string> propertyMappings)
    {
        return new PropertyMappingConverter<TestMessage>(fromVersion, toVersion, propertyMappings, _logger);
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
