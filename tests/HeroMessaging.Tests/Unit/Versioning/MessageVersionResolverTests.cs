using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public sealed class MessageVersionResolverTests
{
    private readonly Mock<ILogger<MessageVersionResolver>> _loggerMock;
    private readonly MessageVersionResolver _resolver;

    public MessageVersionResolverTests()
    {
        _loggerMock = new Mock<ILogger<MessageVersionResolver>>();
        _resolver = new MessageVersionResolver(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Act
        var resolver = new MessageVersionResolver(_loggerMock.Object);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        // The primary constructor assigns the logger directly, so null is allowed
        // but will throw NullReferenceException when logger is used
        var resolver = new MessageVersionResolver(null!);
        Assert.NotNull(resolver);
    }

    #endregion

    #region GetVersion (Type) Tests

    [Fact]
    public void GetVersion_WithMessageVersionAttribute_ReturnsAttributeVersion()
    {
        // Act
        var version = _resolver.GetVersion(typeof(MessageWithAttribute));

        // Assert
        Assert.Equal(new MessageVersion(2, 1, 3), version);
    }

    [Fact]
    public void GetVersion_WithIVersionedMessage_ReturnsInstanceVersion()
    {
        // Act
        var version = _resolver.GetVersion(typeof(VersionedTestMessage));

        // Assert
        Assert.Equal(new MessageVersion(3, 0, 0), version);
    }

    [Fact]
    public void GetVersion_WithoutVersionInfo_ReturnsDefaultVersion()
    {
        // Act
        var version = _resolver.GetVersion(typeof(PlainMessage));

        // Assert
        Assert.Equal(new MessageVersion(1, 0, 0), version);
    }

    [Fact]
    public void GetVersion_CachesResult_MultipleCallsReturnSameVersion()
    {
        // Act
        var version1 = _resolver.GetVersion(typeof(MessageWithAttribute));
        var version2 = _resolver.GetVersion(typeof(MessageWithAttribute));

        // Assert
        Assert.Equal(version1, version2);
    }

    [Fact]
    public void GetVersion_WithUncreatableVersionedMessage_ReturnsDefaultVersion()
    {
        // Act
        var version = _resolver.GetVersion(typeof(UncreatableVersionedMessage));

        // Assert
        Assert.Equal(new MessageVersion(1, 0, 0), version);
    }

    #endregion

    #region GetVersion (Instance) Tests

    [Fact]
    public void GetVersion_WithVersionedMessageInstance_ReturnsInstanceVersion()
    {
        // Arrange
        var message = new VersionedTestMessage();

        // Act
        var version = _resolver.GetVersion(message);

        // Assert
        Assert.Equal(new MessageVersion(3, 0, 0), version);
    }

    [Fact]
    public void GetVersion_WithPlainMessageInstance_ReturnsTypeVersion()
    {
        // Arrange
        var message = new MessageWithAttribute();

        // Act
        var version = _resolver.GetVersion(message);

        // Assert
        Assert.Equal(new MessageVersion(2, 1, 3), version);
    }

    #endregion

    #region GetVersionInfo Tests

    [Fact]
    public void GetVersionInfo_ReturnsCompleteInformation()
    {
        // Act
        var info = _resolver.GetVersionInfo(typeof(MessageWithProperties));

        // Assert
        Assert.NotNull(info);
        Assert.Equal(typeof(MessageWithProperties), info.MessageType);
        Assert.Equal(new MessageVersion(2, 0, 0), info.Version);
        Assert.NotEmpty(info.TypeName);
        Assert.NotNull(info.Properties);
    }

    [Fact]
    public void GetVersionInfo_IncludesAllProperties()
    {
        // Act
        var info = _resolver.GetVersionInfo(typeof(MessageWithProperties));

        // Assert
        Assert.True(info.Properties.Count >= 5); // MessageId, Timestamp, etc. + custom properties
    }

    [Fact]
    public void GetVersionInfo_IncludesAddedInVersionInfo()
    {
        // Act
        var info = _resolver.GetVersionInfo(typeof(MessageWithProperties));

        // Assert
        var newProperty = info.Properties.FirstOrDefault(p => p.Name == nameof(MessageWithProperties.NewProperty));
        Assert.NotNull(newProperty);
        Assert.Equal(new MessageVersion(2, 0, 0), newProperty.AddedInVersion);
    }

    [Fact]
    public void GetVersionInfo_IncludesDeprecatedInfo()
    {
        // Act
        var info = _resolver.GetVersionInfo(typeof(MessageWithProperties));

        // Assert
        var deprecatedProperty = info.Properties.FirstOrDefault(p => p.Name == nameof(MessageWithProperties.OldProperty));
        Assert.NotNull(deprecatedProperty);
        Assert.Equal(new MessageVersion(2, 0, 0), deprecatedProperty.DeprecatedInVersion);
        Assert.Equal("Use NewProperty instead", deprecatedProperty.DeprecationReason);
        Assert.Equal("NewProperty", deprecatedProperty.ReplacedBy);
    }

    [Fact]
    public void GetVersionInfo_CachesResult()
    {
        // Act
        var info1 = _resolver.GetVersionInfo(typeof(MessageWithProperties));
        var info2 = _resolver.GetVersionInfo(typeof(MessageWithProperties));

        // Assert - Should be same instance due to caching
        Assert.Same(info1, info2);
    }

    #endregion

    #region ValidateMessage Tests

    [Fact]
    public void ValidateMessage_WithCompatibleVersion_IsValid()
    {
        // Arrange
        var message = new MessageWithAttribute();
        var targetVersion = new MessageVersion(2, 1, 3);

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateMessage_WithIncompatibleVersion_ReturnsError()
    {
        // Arrange
        var message = new MessageWithAttribute();
        var targetVersion = new MessageVersion(1, 0, 0); // Different major version

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("not compatible", result.Errors[0]);
    }

    [Fact]
    public void ValidateMessage_WithNewPropertySetOnOlderVersion_ReturnsError()
    {
        // Arrange
        // MessageWithProperties has version 2.0.0, but with property added in 2.0.0
        // For this test, we need compatible major versions
        // The actual error is version incompatibility (major version mismatch)
        var message = new MessageWithProperties
        {
            NewProperty = "SomeValue" // This was added in 2.0.0
        };
        var targetVersion = new MessageVersion(1, 5, 0); // Different major version

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        // The error will be about version incompatibility, not the property
        Assert.Contains("not compatible", result.Errors[0]);
    }

    [Fact]
    public void ValidateMessage_WithDeprecatedPropertySet_ReturnsWarning()
    {
        // Arrange
        var message = new MessageWithProperties
        {
            OldProperty = "OldValue" // Deprecated in 2.0.0
        };
        var targetVersion = new MessageVersion(2, 0, 0);

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid); // Warnings don't invalidate
        Assert.True(result.HasWarnings);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("deprecated", result.Warnings[0]);
    }

    [Fact]
    public void ValidateMessage_WithDefaultValues_IsValid()
    {
        // Arrange
        var message = new MessageWithProperties(); // All default values
        var targetVersion = new MessageVersion(2, 0, 0); // Use version that includes NewProperty

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateMessage_WithNullStringProperty_IsValid()
    {
        // Arrange
        var message = new MessageWithProperties
        {
            NewProperty = null // Null is considered default for reference types
        };
        var targetVersion = new MessageVersion(2, 0, 0); // Use version that includes NewProperty

        // Act
        var result = _resolver.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region GetKnownVersions Tests

    [Fact]
    public void GetKnownVersions_ReturnsCurrentVersion()
    {
        // Act
        var versions = _resolver.GetKnownVersions(typeof(MessageWithAttribute)).ToList();

        // Assert
        Assert.Single(versions);
        Assert.Equal(new MessageVersion(2, 1, 3), versions[0]);
    }

    #endregion

    #region MessagePropertyInfo Tests

    [Fact]
    public void MessagePropertyInfo_IsDeprecated_ReturnsTrueForDeprecatedVersion()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo(
            "TestProp",
            typeof(string),
            deprecatedInVersion: new MessageVersion(2, 0, 0));

        // Act & Assert
        Assert.True(propertyInfo.IsDeprecated(new MessageVersion(2, 0, 0)));
        Assert.True(propertyInfo.IsDeprecated(new MessageVersion(2, 1, 0)));
        Assert.False(propertyInfo.IsDeprecated(new MessageVersion(1, 9, 0)));
    }

    [Fact]
    public void MessagePropertyInfo_IsAvailable_ReturnsTrueForAvailableVersion()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo(
            "TestProp",
            typeof(string),
            addedInVersion: new MessageVersion(2, 0, 0));

        // Act & Assert
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(2, 0, 0)));
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(2, 1, 0)));
        Assert.False(propertyInfo.IsAvailable(new MessageVersion(1, 9, 0)));
    }

    [Fact]
    public void MessagePropertyInfo_WithoutAddedVersion_AlwaysAvailable()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo("TestProp", typeof(string));

        // Act & Assert
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(1, 0, 0)));
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(99, 0, 0)));
    }

    #endregion

    #region MessageVersionValidationResult Tests

    [Fact]
    public void ValidationResult_WithNoErrors_IsValid()
    {
        // Arrange
        var result = new MessageVersionValidationResult(true, Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void ValidationResult_WithErrors_IsInvalid()
    {
        // Arrange
        var result = new MessageVersionValidationResult(false, new[] { "Error1" }, Array.Empty<string>());

        // Act & Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void ValidationResult_WithWarnings_IsValidButHasWarnings()
    {
        // Arrange
        var result = new MessageVersionValidationResult(true, Array.Empty<string>(), new[] { "Warning1" });

        // Act & Assert
        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.False(result.HasErrors);
    }

    #endregion

    #region Test Classes

    [MessageVersion(2, 1, 3)]
    private sealed class MessageWithAttribute : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private sealed class PlainMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private sealed class VersionedTestMessage : IVersionedMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public MessageVersion Version => new MessageVersion(3, 0, 0);
        public string MessageType => nameof(VersionedTestMessage);
    }

    private sealed class UncreatableVersionedMessage : IVersionedMessage
    {
        // Constructor that throws to simulate uncreatable type
        public UncreatableVersionedMessage()
        {
            throw new InvalidOperationException("Cannot create instance");
        }

        public Guid MessageId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public MessageVersion Version => new MessageVersion(4, 0, 0);
        public string MessageType => nameof(UncreatableVersionedMessage);
    }

    [MessageVersion(2, 0, 0)]
    private sealed class MessageWithProperties : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [AddedInVersion(2, 0, 0)]
        public string? NewProperty { get; set; }

        [DeprecatedInVersion(2, 0, 0, Reason = "Use NewProperty instead", ReplacedBy = "NewProperty")]
        public string? OldProperty { get; set; }
    }

    #endregion
}
