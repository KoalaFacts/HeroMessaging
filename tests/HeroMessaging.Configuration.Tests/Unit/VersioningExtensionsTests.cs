using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class VersioningExtensionsTests
{
    [Fact]
    public void WithVersioning_WithDefaultOptions_RegistersVersioningServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithVersioning();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(builder, result);
        Assert.NotNull(provider.GetService<IMessageVersionResolver>());
        Assert.NotNull(provider.GetService<IMessageConverterRegistry>());
        Assert.NotNull(provider.GetService<IVersionedMessageService>());
        Assert.NotNull(provider.GetService<MessageVersioningOptions>());
    }

    [Fact]
    public void WithVersioning_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var options = new MessageVersioningOptions
        {
            EnableAutomaticConversion = false,
            StrictValidation = false
        };

        // Act
        builder.WithVersioning(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var registeredOptions = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(registeredOptions);
        Assert.False(registeredOptions.EnableAutomaticConversion);
        Assert.False(registeredOptions.StrictValidation);
    }

    [Fact]
    public void WithVersioning_WithConfigureAction_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithVersioning(opt =>
        {
            opt.EnableAutomaticConversion = false;
            opt.MaxConversionSteps = 10;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(options);
        Assert.False(options.EnableAutomaticConversion);
        Assert.Equal(10, options.MaxConversionSteps);
    }

    [Fact]
    public void WithVersioning_WithInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.WithVersioning());
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void WithDevelopmentVersioning_ConfiguresForDevelopment()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithDevelopmentVersioning();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(options);
        Assert.True(options.EnableAutomaticConversion);
        Assert.False(options.StrictValidation);
        Assert.Equal(VersionCompatibilityMode.Backward, options.DefaultCompatibilityMode);
        Assert.True(options.LogVersioningActivity);
    }

    [Fact]
    public void WithProductionVersioning_ConfiguresForProduction()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithProductionVersioning();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(options);
        Assert.True(options.EnableAutomaticConversion);
        Assert.True(options.StrictValidation);
        Assert.Equal(VersionCompatibilityMode.Strict, options.DefaultCompatibilityMode);
        Assert.False(options.LogVersioningActivity);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ConversionTimeout);
    }

    [Fact]
    public void WithBackwardCompatibleVersioning_ConfiguresBackwardCompatibility()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBackwardCompatibleVersioning();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(options);
        Assert.True(options.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Backward, options.DefaultCompatibilityMode);
        Assert.True(options.AllowVersionDowngrades);
        Assert.False(options.StrictValidation);
    }

    [Fact]
    public void WithForwardCompatibleVersioning_ConfiguresForwardCompatibility()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithForwardCompatibleVersioning();
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<MessageVersioningOptions>();
        Assert.NotNull(options);
        Assert.True(options.EnableAutomaticConversion);
        Assert.Equal(VersionCompatibilityMode.Forward, options.DefaultCompatibilityMode);
        Assert.True(options.IgnoreUnknownProperties);
        Assert.False(options.StrictValidation);
    }

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
    public void VersionCompatibilityMode_HasExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)VersionCompatibilityMode.Strict);
        Assert.Equal(1, (int)VersionCompatibilityMode.Backward);
        Assert.Equal(2, (int)VersionCompatibilityMode.Forward);
        Assert.Equal(3, (int)VersionCompatibilityMode.Flexible);
    }

    [Fact]
    public void MessageVersioningProfiles_Microservices_HasCorrectConfiguration()
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
    public void MessageVersioningProfiles_Monolith_HasCorrectConfiguration()
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
    public void MessageVersioningProfiles_EventSourcing_HasCorrectConfiguration()
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
    public void MessageVersioningProfiles_HighPerformance_HasCorrectConfiguration()
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
    public void MessageVersioningProfiles_Development_HasCorrectConfiguration()
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
    public void RegisterConverter_WithInvalidBuilder_ThrowsArgumentException()
    {
        // Arrange
        var mockBuilder = new Mock<IHeroMessagingBuilder>();
        var mockConverter = new Mock<IMessageConverter<TestMessage>>().Object;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => mockBuilder.Object.RegisterConverter(mockConverter));
        Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
        Assert.Equal("builder", ex.ParamName);
    }

    [Fact]
    public void RegisterConverter_RegistersConverterRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var mockConverter = new Mock<IMessageConverter<TestMessage>>().Object;

        // Act
        builder.RegisterConverter(mockConverter);
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IMessageConverterRegistry>());
    }

    [Fact]
    public void RegisterConverters_RegistersMultipleConverters()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);
        var converter1 = new Mock<IMessageConverter<TestMessage>>().Object;
        var converter2 = new Mock<IMessageConverter<TestMessage>>().Object;

        // Act
        var result = builder.RegisterConverters(converter1, converter2);

        // Assert
        Assert.Same(builder, result);
    }

    // Test helper class
    private sealed class TestMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string MessageType { get; set; } = nameof(TestMessage);
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
