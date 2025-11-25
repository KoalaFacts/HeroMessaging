using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
public class MessageVersionResolverTests
{
    private readonly Mock<ILogger<MessageVersionResolver>> _mockLogger;
    private readonly MessageVersionResolver _sut;

    public MessageVersionResolverTests()
    {
        _mockLogger = new Mock<ILogger<MessageVersionResolver>>();
        _sut = new MessageVersionResolver(_mockLogger.Object);
    }

    #region GetVersion(Type) Tests

    [Fact]
    public void GetVersion_WithMessageVersionAttribute_ReturnsAttributeVersion()
    {
        // Arrange
        var messageType = typeof(MessageWithAttribute);

        // Act
        var version = _sut.GetVersion(messageType);

        // Assert
        Assert.Equal(2, version.Major);
        Assert.Equal(1, version.Minor);
        Assert.Equal(3, version.Patch);
    }

    [Fact]
    public void GetVersion_WithIVersionedMessageImplementation_ReturnsInstanceVersion()
    {
        // Arrange
        var messageType = typeof(VersionedMessageImpl);

        // Act
        var version = _sut.GetVersion(messageType);

        // Assert
        Assert.Equal(3, version.Major);
        Assert.Equal(0, version.Minor);
        Assert.Equal(0, version.Patch);
    }

    [Fact]
    public void GetVersion_WithNoVersioning_ReturnsDefaultVersion()
    {
        // Arrange
        var messageType = typeof(PlainMessage);

        // Act
        var version = _sut.GetVersion(messageType);

        // Assert
        Assert.Equal(1, version.Major);
        Assert.Equal(0, version.Minor);
        Assert.Equal(0, version.Patch);
    }

    [Fact]
    public void GetVersion_CachesResults_ReturnsSameInstanceOnMultipleCalls()
    {
        // Arrange
        var messageType = typeof(MessageWithAttribute);

        // Act
        var version1 = _sut.GetVersion(messageType);
        var version2 = _sut.GetVersion(messageType);

        // Assert
        Assert.Equal(version1, version2);
    }

    #endregion

    #region GetVersion(IMessage) Tests

    [Fact]
    public void GetVersion_WithVersionedMessageInstance_ReturnsInstanceVersion()
    {
        // Arrange
        var message = new VersionedMessageImpl();

        // Act
        var version = _sut.GetVersion(message);

        // Assert
        Assert.Equal(3, version.Major);
        Assert.Equal(0, version.Minor);
        Assert.Equal(0, version.Patch);
    }

    [Fact]
    public void GetVersion_WithNonVersionedMessageInstance_ReturnsTypeVersion()
    {
        // Arrange
        var message = new MessageWithAttribute();

        // Act
        var version = _sut.GetVersion(message);

        // Assert
        Assert.Equal(2, version.Major);
        Assert.Equal(1, version.Minor);
        Assert.Equal(3, version.Patch);
    }

    #endregion

    #region GetVersionInfo Tests

    [Fact]
    public void GetVersionInfo_ReturnsCompleteInformation()
    {
        // Arrange
        var messageType = typeof(MessageWithProperties);

        // Act
        var versionInfo = _sut.GetVersionInfo(messageType);

        // Assert
        Assert.Equal(messageType, versionInfo.MessageType);
        Assert.Equal(new MessageVersion(1, 5, 0), versionInfo.Version);
        Assert.NotEmpty(versionInfo.Properties);
        Assert.Contains(versionInfo.Properties, p => p.Name == "NewProperty");
    }

    [Fact]
    public void GetVersionInfo_IncludesPropertyVersionInformation()
    {
        // Arrange
        var messageType = typeof(MessageWithProperties);

        // Act
        var versionInfo = _sut.GetVersionInfo(messageType);
        var newProperty = versionInfo.Properties.FirstOrDefault(p => p.Name == "NewProperty");

        // Assert
        Assert.NotNull(newProperty);
        Assert.Equal(new MessageVersion(1, 5, 0), newProperty.AddedInVersion);
    }

    [Fact]
    public void GetVersionInfo_IncludesDeprecatedPropertyInformation()
    {
        // Arrange
        var messageType = typeof(MessageWithProperties);

        // Act
        var versionInfo = _sut.GetVersionInfo(messageType);
        var oldProperty = versionInfo.Properties.FirstOrDefault(p => p.Name == "OldProperty");

        // Assert
        Assert.NotNull(oldProperty);
        Assert.Equal(new MessageVersion(2, 0, 0), oldProperty.DeprecatedInVersion);
        Assert.Equal("Use NewProperty instead", oldProperty.DeprecationReason);
        Assert.Equal("NewProperty", oldProperty.ReplacedBy);
    }

    [Fact]
    public void GetVersionInfo_CachesResults_ReturnsSameInstanceOnMultipleCalls()
    {
        // Arrange
        var messageType = typeof(MessageWithProperties);

        // Act
        var info1 = _sut.GetVersionInfo(messageType);
        var info2 = _sut.GetVersionInfo(messageType);

        // Assert
        Assert.Same(info1, info2);
    }

    #endregion

    #region ValidateMessage Tests

    [Fact]
    public void ValidateMessage_WithCompatibleVersion_ReturnsValid()
    {
        // Arrange
        var message = new MessageWithAttribute();
        var targetVersion = new MessageVersion(2, 0, 0);

        // Act
        var result = _sut.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateMessage_WithIncompatibleMajorVersion_ReturnsInvalid()
    {
        // Arrange
        var message = new MessageWithAttribute(); // Version 2.1.3
        var targetVersion = new MessageVersion(3, 0, 0);

        // Act
        var result = _sut.ValidateMessage(message, targetVersion);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("not compatible", result.Errors[0]);
    }

    [Fact]
    public void ValidateMessage_WithPropertyAddedAfterTargetVersion_ReturnsError()
    {
        // Arrange
        var message = new MessageWithProperties
        {
            NewProperty = "Has value"
        };
        var targetVersion = new MessageVersion(1, 4, 0); // Before NewProperty was added

        // Act
        var result = _sut.ValidateMessage(message, targetVersion);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("NewProperty") && e.Contains("added in version"));
    }

    [Fact]
    public void ValidateMessage_WithDeprecatedPropertyUsed_ReturnsWarning()
    {
        // Arrange
        var message = new MessageWithProperties
        {
            OldProperty = "Still using deprecated"
        };
        var targetVersion = new MessageVersion(2, 0, 0);

        // Act
        var result = _sut.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("OldProperty") && w.Contains("deprecated"));
        Assert.Contains(result.Warnings, w => w.Contains("Use NewProperty instead"));
        Assert.Contains(result.Warnings, w => w.Contains("replaced by NewProperty"));
    }

    [Fact]
    public void ValidateMessage_WithDefaultPropertyValues_DoesNotRaiseErrors()
    {
        // Arrange
        var message = new MessageWithProperties(); // All properties at default
        var targetVersion = new MessageVersion(1, 0, 0);

        // Act
        var result = _sut.ValidateMessage(message, targetVersion);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region GetKnownVersions Tests

    [Fact]
    public void GetKnownVersions_ReturnsCurrentVersion()
    {
        // Arrange
        var messageType = typeof(MessageWithAttribute);

        // Act
        var versions = _sut.GetKnownVersions(messageType).ToList();

        // Assert
        Assert.Single(versions);
        Assert.Equal(new MessageVersion(2, 1, 3), versions[0]);
    }

    #endregion

    #region MessageVersionInfo Tests

    [Fact]
    public void MessageVersionInfo_Constructor_SetsAllProperties()
    {
        // Arrange
        var messageType = typeof(PlainMessage);
        var version = new MessageVersion(1, 0, 0);
        var typeName = "TestType";
        var properties = new List<MessagePropertyInfo>().AsReadOnly();

        // Act
        var info = new MessageVersionInfo(messageType, version, typeName, properties);

        // Assert
        Assert.Equal(messageType, info.MessageType);
        Assert.Equal(version, info.Version);
        Assert.Equal(typeName, info.TypeName);
        Assert.Same(properties, info.Properties);
    }

    #endregion

    #region MessagePropertyInfo Tests

    [Fact]
    public void MessagePropertyInfo_IsDeprecated_ReturnsTrueWhenDeprecatedVersionReached()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo(
            "TestProperty",
            typeof(string),
            deprecatedInVersion: new MessageVersion(2, 0, 0));

        // Act & Assert
        Assert.True(propertyInfo.IsDeprecated(new MessageVersion(2, 0, 0)));
        Assert.True(propertyInfo.IsDeprecated(new MessageVersion(2, 1, 0)));
        Assert.False(propertyInfo.IsDeprecated(new MessageVersion(1, 9, 0)));
    }

    [Fact]
    public void MessagePropertyInfo_IsAvailable_ReturnsTrueWhenVersionReached()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo(
            "TestProperty",
            typeof(string),
            addedInVersion: new MessageVersion(1, 5, 0));

        // Act & Assert
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(1, 5, 0)));
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(2, 0, 0)));
        Assert.False(propertyInfo.IsAvailable(new MessageVersion(1, 4, 0)));
    }

    [Fact]
    public void MessagePropertyInfo_IsAvailable_ReturnsTrueWhenNoAddedVersion()
    {
        // Arrange
        var propertyInfo = new MessagePropertyInfo("TestProperty", typeof(string));

        // Act & Assert
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(0, 0, 1)));
        Assert.True(propertyInfo.IsAvailable(new MessageVersion(99, 99, 99)));
    }

    #endregion

    #region MessageVersionValidationResult Tests

    [Fact]
    public void MessageVersionValidationResult_WithNoErrors_IsValid()
    {
        // Arrange & Act
        var result = new MessageVersionValidationResult(
            true,
            new List<string>().AsReadOnly(),
            new List<string>().AsReadOnly());

        // Assert
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void MessageVersionValidationResult_WithErrors_IsInvalid()
    {
        // Arrange & Act
        var result = new MessageVersionValidationResult(
            false,
            new List<string> { "Error 1", "Error 2" }.AsReadOnly(),
            new List<string>().AsReadOnly());

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void MessageVersionValidationResult_WithWarnings_IsValidButHasWarnings()
    {
        // Arrange & Act
        var result = new MessageVersionValidationResult(
            true,
            new List<string>().AsReadOnly(),
            new List<string> { "Warning 1" }.AsReadOnly());

        // Assert
        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
    }

    #endregion

    #region Test Message Classes

    [MessageVersion(2, 1, 3)]
    private class MessageWithAttribute : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string MessageType => GetType().Name;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class VersionedMessageImpl : IVersionedMessage
    {
        public MessageVersion Version => new MessageVersion(3, 0, 0);
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string MessageType => GetType().Name;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    private class PlainMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    [MessageVersion(1, 5, 0)]
    private class MessageWithProperties : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }

        [AddedInVersion(1, 5, 0)]
        public string? NewProperty { get; set; }

        [DeprecatedInVersion(2, 0, 0, Reason = "Use NewProperty instead", ReplacedBy = "NewProperty")]
        public string? OldProperty { get; set; }

        public string? RegularProperty { get; set; }
    }

    #endregion
}
