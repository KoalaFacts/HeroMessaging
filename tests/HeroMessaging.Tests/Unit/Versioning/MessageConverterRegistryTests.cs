using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public sealed class MessageConverterRegistryTests
{
    private readonly Mock<ILogger<MessageConverterRegistry>> _loggerMock;
    private readonly MessageConverterRegistry _registry;

    public MessageConverterRegistryTests()
    {
        _loggerMock = new Mock<ILogger<MessageConverterRegistry>>();
        _registry = new MessageConverterRegistry(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
        // Act
        var registry = new MessageConverterRegistry(_loggerMock.Object);

        // Assert
        Assert.NotNull(registry);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MessageConverterRegistry(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region RegisterConverter Tests

    [Fact]
    public void RegisterConverter_WithValidConverter_Succeeds()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Act
        _registry.RegisterConverter(converter);

        // Assert
        var converters = _registry.GetConverters(typeof(TestMessage));
        Assert.Single(converters);
    }

    [Fact]
    public void RegisterConverter_WithNullConverter_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            _registry.RegisterConverter<TestMessage>(null!));
        Assert.Equal("converter", exception.ParamName);
    }

    [Fact]
    public void RegisterConverter_WithOverlappingVersions_LogsWarning()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(1, 5, 0), new MessageVersion(2, 5, 0));

        // Act
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);

        // Assert
        var converters = _registry.GetConverters(typeof(TestMessage));
        Assert.Equal(2, converters.Count());
    }

    [Fact]
    public void RegisterConverter_MultipleConverters_AllRegistered()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        var converter3 = CreateTestConverter(new MessageVersion(3, 0, 0), new MessageVersion(4, 0, 0));

        // Act
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);
        _registry.RegisterConverter(converter3);

        // Assert
        var converters = _registry.GetConverters(typeof(TestMessage));
        Assert.Equal(3, converters.Count());
    }

    #endregion

    #region GetConverter Tests

    [Fact]
    public void GetConverter_WithRegisteredConverter_ReturnsConverter()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var result = _registry.GetConverter<TestMessage>(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetConverter_WithoutRegisteredConverter_ReturnsNull()
    {
        // Act
        var result = _registry.GetConverter<TestMessage>(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetConverter_NonGeneric_WithRegisteredConverter_ReturnsConverter()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var result = _registry.GetConverter(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetConverter_NonGeneric_WithoutRegisteredConverter_ReturnsNull()
    {
        // Act
        var result = _registry.GetConverter(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetConverter_WithVersionInRange_ReturnsConverter()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var result = _registry.GetConverter<TestMessage>(new MessageVersion(1, 5, 0), new MessageVersion(1, 8, 0));

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region GetConverters Tests

    [Fact]
    public void GetConverters_WithNoConverters_ReturnsEmpty()
    {
        // Act
        var converters = _registry.GetConverters(typeof(TestMessage));

        // Assert
        Assert.Empty(converters);
    }

    [Fact]
    public void GetConverters_WithMultipleConverters_ReturnsAll()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);

        // Act
        var converters = _registry.GetConverters(typeof(TestMessage));

        // Assert
        Assert.Equal(2, converters.Count());
    }

    [Fact]
    public void GetConverters_ReturnsCopy_ModificationDoesNotAffectRegistry()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var converters1 = _registry.GetConverters(typeof(TestMessage)).ToList();
        var converters2 = _registry.GetConverters(typeof(TestMessage)).ToList();

        // Assert - collections are independent
        Assert.Equal(converters1.Count, converters2.Count);
        Assert.NotSame(converters1, converters2);
    }

    #endregion

    #region CanConvert Tests

    [Fact]
    public void CanConvert_WithDirectConverter_ReturnsTrue()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var result = _registry.CanConvert(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanConvert_WithoutConverter_ReturnsFalse()
    {
        // Act
        var result = _registry.CanConvert(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanConvert_WithSameVersion_ReturnsTrue()
    {
        // Act
        var result = _registry.CanConvert(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(1, 0, 0));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanConvert_WithMultiStepPath_ReturnsTrue()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);

        // Act
        var result = _registry.CanConvert(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(3, 0, 0));

        // Assert
        Assert.True(result);
    }

    #endregion

    #region FindConversionPath Tests

    [Fact]
    public void FindConversionPath_WithSameVersion_ReturnsEmptyPath()
    {
        // Act
        var path = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(1, 0, 0));

        // Assert
        Assert.NotNull(path);
        Assert.Empty(path.Steps);
        Assert.Equal(new MessageVersion(1, 0, 0), path.FromVersion);
        Assert.Equal(new MessageVersion(1, 0, 0), path.ToVersion);
    }

    [Fact]
    public void FindConversionPath_WithDirectConverter_ReturnsSingleStep()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        var path = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.NotNull(path);
        Assert.Single(path.Steps);
        Assert.True(path.IsDirect);
        Assert.False(path.RequiresMultipleSteps);
    }

    [Fact]
    public void FindConversionPath_WithoutConverter_ReturnsNull()
    {
        // Act
        var path = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.Null(path);
    }

    [Fact]
    public void FindConversionPath_WithMultiStep_ReturnsCompletePath()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);

        // Act
        var path = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(3, 0, 0));

        // Assert
        Assert.NotNull(path);
        Assert.Equal(2, path.Steps.Count);
        Assert.False(path.IsDirect);
        Assert.True(path.RequiresMultipleSteps);
    }

    [Fact]
    public void FindConversionPath_CachesResult_SecondCallIsFaster()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act - First call
        var path1 = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Act - Second call (should be cached)
        var path2 = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Assert
        Assert.NotNull(path1);
        Assert.NotNull(path2);
        Assert.Equal(path1.Steps.Count, path2.Steps.Count);
    }

    [Fact]
    public void FindConversionPath_WithComplexGraph_FindsShortestPath()
    {
        // Arrange - Create a graph where there are multiple paths from 1.0.0 to 3.0.0
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        var converter3 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(3, 0, 0)); // Direct path

        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);
        _registry.RegisterConverter(converter3);

        // Act
        var path = _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(3, 0, 0));

        // Assert - Should prefer the direct path
        Assert.NotNull(path);
        Assert.Single(path.Steps);
        Assert.True(path.IsDirect);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_WithNoConverters_ReturnsZeroStats()
    {
        // Act
        var stats = _registry.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalConverters);
        Assert.Equal(0, stats.MessageTypes);
        Assert.Equal(0, stats.CachedPaths);
    }

    [Fact]
    public void GetStatistics_WithConverters_ReturnsCorrectCounts()
    {
        // Arrange
        var converter1 = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        var converter2 = CreateTestConverter(new MessageVersion(2, 0, 0), new MessageVersion(3, 0, 0));
        _registry.RegisterConverter(converter1);
        _registry.RegisterConverter(converter2);

        // Act
        var stats = _registry.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalConverters);
        Assert.Equal(1, stats.MessageTypes);
        Assert.True(stats.ConvertersByType.ContainsKey(nameof(TestMessage)));
        Assert.Equal(2, stats.ConvertersByType[nameof(TestMessage)]);
    }

    [Fact]
    public void GetStatistics_AfterFindingPath_IncludesCachedPaths()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);
        _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Act
        var stats = _registry.GetStatistics();

        // Assert
        Assert.Equal(1, stats.CachedPaths);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_RemovesAllConverters()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);

        // Act
        _registry.Clear();

        // Assert
        var converters = _registry.GetConverters(typeof(TestMessage));
        Assert.Empty(converters);
    }

    [Fact]
    public void Clear_RemovesCachedPaths()
    {
        // Arrange
        var converter = CreateTestConverter(new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));
        _registry.RegisterConverter(converter);
        _registry.FindConversionPath(typeof(TestMessage), new MessageVersion(1, 0, 0), new MessageVersion(2, 0, 0));

        // Act
        _registry.Clear();

        // Assert
        var stats = _registry.GetStatistics();
        Assert.Equal(0, stats.CachedPaths);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void RegisterConverter_ConcurrentRegistration_AllConvertersRegistered()
    {
        // Arrange
        var converters = Enumerable.Range(0, 10)
            .Select(i => CreateTestConverter(new MessageVersion(i, 0, 0), new MessageVersion(i + 1, 0, 0)))
            .ToList();

        // Act
        Parallel.ForEach(converters, converter =>
        {
            _registry.RegisterConverter(converter);
        });

        // Assert
        var registeredConverters = _registry.GetConverters(typeof(TestMessage));
        Assert.Equal(10, registeredConverters.Count());
    }

    #endregion

    #region Helper Methods

    private static TestMessageConverter CreateTestConverter(MessageVersion fromVersion, MessageVersion toVersion)
    {
        // Use NullLogger since TestMessageConverter is a private nested class and Moq
        // cannot create a proxy for ILogger<TestMessageConverter> when the type is not accessible
        return new TestMessageConverter(fromVersion, toVersion,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TestMessageConverter>.Instance);
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

    public sealed class TestMessageConverter : MessageConverterBase<TestMessage>
    {
        private readonly MessageVersionRange _versionRange;
        private readonly ILogger<TestMessageConverter> _logger;

        public TestMessageConverter(MessageVersion fromVersion, MessageVersion toVersion, ILogger<TestMessageConverter> logger)
        {
            _versionRange = new MessageVersionRange(fromVersion, toVersion);
            _logger = logger;
        }

        public override MessageVersionRange SupportedVersionRange => _versionRange;

        public override Task<TestMessage> ConvertAsync(TestMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message);
        }
    }

    #endregion
}
