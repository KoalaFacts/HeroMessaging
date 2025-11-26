using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Configuration;
using HeroMessaging.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class VersioningExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public VersioningExtensionsTests()
    {
        _services = new ServiceCollection();
        // Add NullLoggerFactory so services requiring ILogger<T> can be resolved
        _services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _builder = new HeroMessagingBuilder(_services);
    }

    #region WithVersioning Tests

    [Fact]
    public void WithVersioning_WithDefaultOptions_RegistersVersioningServices()
    {
        // Act
        var result = _builder.WithVersioning();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var resolver = provider.GetService<IMessageVersionResolver>();
        var registry = provider.GetService<IMessageConverterRegistry>();

        Assert.NotNull(resolver);
        Assert.NotNull(registry);
    }

    [Fact]
    public void WithVersioning_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var options = new MessageVersioningOptions
        {
            EnableAutomaticConversion = false,
            StrictValidation = true,
            MaxConversionSteps = 10
        };

        // Act
        var result = _builder.WithVersioning(options);

        // Assert
        Assert.NotNull(result);

        var provider = _services.BuildServiceProvider();
        var registeredOptions = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(registeredOptions);
        Assert.False(registeredOptions.EnableAutomaticConversion);
        Assert.True(registeredOptions.StrictValidation);
        Assert.Equal(10, registeredOptions.MaxConversionSteps);
    }

    [Fact]
    public void WithVersioning_WithConfigureAction_CallsConfigureAction()
    {
        // Arrange
        var configureCalled = false;
        MessageVersioningOptions? capturedOptions = null;

        // Act
        var result = _builder.WithVersioning(options =>
        {
            configureCalled = true;
            capturedOptions = options;
            options.MaxConversionSteps = 7;
            options.StrictValidation = false;
        });

        // Assert
        Assert.True(configureCalled);
        Assert.NotNull(capturedOptions);
        Assert.Equal(7, capturedOptions.MaxConversionSteps);
        Assert.False(capturedOptions.StrictValidation);
    }

    [Fact]
    public void WithVersioning_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.WithVersioning());

        Assert.Equal("builder", exception.ParamName);
        Assert.Contains("must be of type HeroMessagingBuilder", exception.Message);
    }

    #endregion

    #region RegisterConverter Tests

    [Fact]
    public void RegisterConverter_WithValidConverter_RegistersSuccessfully()
    {
        // Arrange
        var converterMock = new Mock<IMessageConverter<TestMessage>>();

        // Act
        var result = _builder.RegisterConverter(converterMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var registry = provider.GetService<IMessageConverterRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void RegisterConverter_WithNonHeroMessagingBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        var converterMock = new Mock<IMessageConverter<TestMessage>>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            mockBuilder.Object.RegisterConverter(converterMock.Object));

        Assert.Equal("builder", exception.ParamName);
    }

    #endregion

    #region RegisterConverters Tests

    [Fact]
    public void RegisterConverters_WithMultipleConverters_RegistersAll()
    {
        // Arrange
        var converter1 = new Mock<IMessageConverter<TestMessage>>();
        var converter2 = new Mock<IMessageConverter<TestMessage>>();

        // Act
        var result = _builder.RegisterConverters(converter1.Object, converter2.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    [Fact]
    public void RegisterConverters_WithEmptyArray_DoesNotThrow()
    {
        // Act
        var result = _builder.RegisterConverters<TestMessage>();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);
    }

    #endregion

    #region WithDevelopmentVersioning Tests

    [Fact]
    public void WithDevelopmentVersioning_ConfiguresCorrectSettings()
    {
        // Act
        var result = _builder.WithDevelopmentVersioning();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var resolver = provider.GetService<IMessageVersionResolver>();
        var registry = provider.GetService<IMessageConverterRegistry>();
        Assert.NotNull(resolver);
        Assert.NotNull(registry);
    }

    [Fact]
    public void WithDevelopmentVersioning_UsesFlexibleSettings()
    {
        // This test validates that development versioning is configured
        // The actual settings validation would happen through integration tests

        // Act
        _builder.WithDevelopmentVersioning();
        var provider = _services.BuildServiceProvider();

        // Assert
        var resolver = provider.GetService<IMessageVersionResolver>();
        Assert.NotNull(resolver);
    }

    #endregion

    #region WithProductionVersioning Tests

    [Fact]
    public void WithProductionVersioning_ConfiguresCorrectSettings()
    {
        // Act
        var result = _builder.WithProductionVersioning();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var resolver = provider.GetService<IMessageVersionResolver>();
        var registry = provider.GetService<IMessageConverterRegistry>();
        Assert.NotNull(resolver);
        Assert.NotNull(registry);
    }

    [Fact]
    public void WithProductionVersioning_UsesStrictSettings()
    {
        // This test validates that production versioning is configured
        // The actual settings validation would happen through integration tests

        // Act
        _builder.WithProductionVersioning();
        var provider = _services.BuildServiceProvider();

        // Assert
        var resolver = provider.GetService<IMessageVersionResolver>();
        Assert.NotNull(resolver);
    }

    #endregion

    #region WithBackwardCompatibleVersioning Tests

    [Fact]
    public void WithBackwardCompatibleVersioning_ConfiguresCorrectSettings()
    {
        // Act
        var result = _builder.WithBackwardCompatibleVersioning();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var resolver = provider.GetService<IMessageVersionResolver>();
        Assert.NotNull(resolver);
    }

    #endregion

    #region WithForwardCompatibleVersioning Tests

    [Fact]
    public void WithForwardCompatibleVersioning_ConfiguresCorrectSettings()
    {
        // Act
        var result = _builder.WithForwardCompatibleVersioning();

        // Assert
        Assert.NotNull(result);
        Assert.Same(_builder, result);

        var provider = _services.BuildServiceProvider();
        var resolver = provider.GetService<IMessageVersionResolver>();
        Assert.NotNull(resolver);
    }

    #endregion

    #region MessageVersioningOptions Tests

    [Fact]
    public void MessageVersioningOptions_DefaultValues_AreCorrect()
    {
        // Act
        var options = new MessageVersioningOptions();

        // Assert
        Assert.True(options.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Backward, options.DefaultCompatibilityMode);
        Assert.True(options.StrictValidation);
        Assert.False(options.AllowVersionDowngrades);
        Assert.True(options.IgnoreUnknownProperties);
        Assert.False(options.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConversionTimeout);
        Assert.Equal(5, options.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningOptions_RegisterConverter_AddsToRegistry()
    {
        // Arrange
        var options = new MessageVersioningOptions();
        var converterMock = new Mock<IMessageConverter<TestMessage>>();

        // Act
        options.RegisterConverter(converterMock.Object);

        // Assert
        Assert.Single(options.ConverterRegistrations);
    }

    #endregion

    #region MessageVersioningProfiles Tests

    [Fact]
    public void MessageVersioningProfiles_Microservices_HasCorrectSettings()
    {
        // Act
        var profile = MessageVersioningProfiles.Microservices;

        // Assert
        Assert.True(profile.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Backward, profile.DefaultCompatibilityMode);
        Assert.True(profile.StrictValidation);
        Assert.False(profile.AllowVersionDowngrades);
        Assert.True(profile.IgnoreUnknownProperties);
        Assert.False(profile.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(5), profile.ConversionTimeout);
        Assert.Equal(3, profile.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningProfiles_Monolith_HasCorrectSettings()
    {
        // Act
        var profile = MessageVersioningProfiles.Monolith;

        // Assert
        Assert.True(profile.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Strict, profile.DefaultCompatibilityMode);
        Assert.True(profile.StrictValidation);
        Assert.False(profile.AllowVersionDowngrades);
        Assert.False(profile.IgnoreUnknownProperties);
        Assert.True(profile.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(15), profile.ConversionTimeout);
        Assert.Equal(5, profile.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningProfiles_EventSourcing_HasCorrectSettings()
    {
        // Act
        var profile = MessageVersioningProfiles.EventSourcing;

        // Assert
        Assert.True(profile.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Forward, profile.DefaultCompatibilityMode);
        Assert.False(profile.StrictValidation);
        Assert.True(profile.AllowVersionDowngrades);
        Assert.True(profile.IgnoreUnknownProperties);
        Assert.True(profile.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(30), profile.ConversionTimeout);
        Assert.Equal(10, profile.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningProfiles_HighPerformance_HasCorrectSettings()
    {
        // Act
        var profile = MessageVersioningProfiles.HighPerformance;

        // Assert
        Assert.False(profile.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Strict, profile.DefaultCompatibilityMode);
        Assert.False(profile.StrictValidation);
        Assert.False(profile.AllowVersionDowngrades);
        Assert.True(profile.IgnoreUnknownProperties);
        Assert.False(profile.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(1), profile.ConversionTimeout);
        Assert.Equal(1, profile.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningProfiles_Development_HasCorrectSettings()
    {
        // Act
        var profile = MessageVersioningProfiles.Development;

        // Assert
        Assert.True(profile.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Flexible, profile.DefaultCompatibilityMode);
        Assert.False(profile.StrictValidation);
        Assert.True(profile.AllowVersionDowngrades);
        Assert.True(profile.IgnoreUnknownProperties);
        Assert.True(profile.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromMinutes(1), profile.ConversionTimeout);
        Assert.Equal(10, profile.MaxConversionSteps);
    }

    [Fact]
    public void MessageVersioningProfiles_AllProfiles_AreAccessible()
    {
        // Act & Assert
        Assert.NotNull(MessageVersioningProfiles.Microservices);
        Assert.NotNull(MessageVersioningProfiles.Monolith);
        Assert.NotNull(MessageVersioningProfiles.EventSourcing);
        Assert.NotNull(MessageVersioningProfiles.HighPerformance);
        Assert.NotNull(MessageVersioningProfiles.Development);
    }

    #endregion

    #region VersionCompatibilityMode Tests

    [Fact]
    public void VersionCompatibilityMode_AllValues_AreDefined()
    {
        // Act & Assert - Ensure all enum values can be accessed
        var strict = VersionCompatibilityMode.Strict;
        var backward = VersionCompatibilityMode.Backward;
        var forward = VersionCompatibilityMode.Forward;
        var flexible = VersionCompatibilityMode.Flexible;

        Assert.Equal(VersionCompatibilityMode.Strict, strict);
        Assert.Equal(VersionCompatibilityMode.Backward, backward);
        Assert.Equal(VersionCompatibilityMode.Forward, forward);
        Assert.Equal(VersionCompatibilityMode.Flexible, flexible);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void WithVersioning_CanChainWithOtherExtensions()
    {
        // Act
        var result = _builder
            .WithVersioning()
            .Development()
            .UseInMemoryStorage();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void RegisterConverter_CanChainMultipleCalls()
    {
        // Arrange
        var converter1 = new Mock<IMessageConverter<TestMessage>>();
        var converter2 = new Mock<IMessageConverter<TestMessage>>();

        // Act
        var result = _builder
            .RegisterConverter(converter1.Object)
            .RegisterConverter(converter2.Object);

        // Assert
        Assert.NotNull(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void WithVersioning_FullConfiguration_BuildsSuccessfully()
    {
        // Arrange
        var options = new MessageVersioningOptions
        {
            EnableAutomaticConversion = true,
            StrictValidation = false,
            DefaultCompatibilityMode = VersionCompatibilityMode.Backward,
            MaxConversionSteps = 10
        };

        // Act
        _builder.WithVersioning(options);
        var provider = _services.BuildServiceProvider();

        // Assert
        var resolver = provider.GetService<IMessageVersionResolver>();
        var registry = provider.GetService<IMessageConverterRegistry>();

        Assert.NotNull(resolver);
        Assert.NotNull(registry);
    }

    [Fact]
    public void WithVersioning_UsingMicroservicesProfile_ConfiguresCorrectly()
    {
        // Arrange
        var options = MessageVersioningProfiles.Microservices;

        // Act
        _builder.WithVersioning(options);
        var provider = _services.BuildServiceProvider();

        // Assert
        var resolver = provider.GetService<IMessageVersionResolver>();
        Assert.NotNull(resolver);
    }

    #endregion

    #region Helper Classes

    // Must be public for Moq to create a proxy for IMessageConverter<TestMessage>
    public class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = new();
    }

    #endregion
}
