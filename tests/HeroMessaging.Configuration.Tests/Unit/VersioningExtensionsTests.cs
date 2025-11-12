using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit
{
    [Trait("Category", "Unit")]
    public sealed class VersioningExtensionsTests
    {
        #region WithVersioning Tests

        [Fact]
        public void WithVersioning_WithNullBuilder_ThrowsArgumentException()
        {
            // Arrange
            IHeroMessagingBuilder builder = null!;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.WithVersioning());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithVersioning_WithWrongBuilderType_ThrowsArgumentException()
        {
            // Arrange
            var builder = new Mock<IHeroMessagingBuilder>().Object;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.WithVersioning());
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void WithVersioning_WithNullOptions_RegistersDefaultServices()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithVersioning();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();

            // Verify options are registered
            var options = provider.GetService<MessageVersioningOptions>();
            Assert.NotNull(options);

            // Verify services were added (count should be > 0)
            Assert.NotEmpty(services);
        }

        [Fact]
        public void WithVersioning_WithCustomOptions_RegistersServicesWithOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);
            var customOptions = new MessageVersioningOptions
            {
                EnableAutomaticConversion = false,
                StrictValidation = false,
                ConversionTimeout = TimeSpan.FromMinutes(1)
            };

            // Act
            var result = builder.WithVersioning(customOptions);

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

            Assert.False(options.EnableAutomaticConversion);
            Assert.False(options.StrictValidation);
            Assert.Equal(TimeSpan.FromMinutes(1), options.ConversionTimeout);
        }

        [Fact]
        public void WithVersioning_WithConfigurationAction_AppliesConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithVersioning(options =>
            {
                options.EnableAutomaticConversion = false;
                options.StrictValidation = true;
                options.DefaultCompatibilityMode = VersionCompatibilityMode.Strict;
                options.ConversionTimeout = TimeSpan.FromSeconds(30);
            });

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

            Assert.False(options.EnableAutomaticConversion);
            Assert.True(options.StrictValidation);
            Assert.Equal(VersionCompatibilityMode.Strict, options.DefaultCompatibilityMode);
            Assert.Equal(TimeSpan.FromSeconds(30), options.ConversionTimeout);
        }

        #endregion

        #region RegisterConverter Tests

        [Fact]
        public void RegisterConverter_WithNullBuilder_ThrowsArgumentException()
        {
            // Arrange
            IHeroMessagingBuilder builder = null!;
            var converter = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.RegisterConverter(converter));
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void RegisterConverter_WithWrongBuilderType_ThrowsArgumentException()
        {
            // Arrange
            var builder = new Mock<IHeroMessagingBuilder>().Object;
            var converter = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => builder.RegisterConverter(converter));
            Assert.Contains("Builder must be of type HeroMessagingBuilder", ex.Message);
            Assert.Equal("builder", ex.ParamName);
        }

        [Fact]
        public void RegisterConverter_RegistersConverterAndEnsuresRegistry()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);
            var converter = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act
            var result = builder.RegisterConverter(converter);

            // Assert
            Assert.Same(builder, result);

            // Verify services were registered
            Assert.NotEmpty(services);
        }

        [Fact]
        public void RegisterConverters_WithMultipleConverters_RegistersAll()
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

            // Verify services were registered
            Assert.NotEmpty(services);
        }

        [Fact]
        public void RegisterConverters_WithEmptyArray_ReturnsSameBuilder()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.RegisterConverters<TestMessage>();

            // Assert
            Assert.Same(builder, result);
        }

        #endregion

        #region Preset Configuration Tests

        [Fact]
        public void WithDevelopmentVersioning_ConfiguresForDevelopment()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithDevelopmentVersioning();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

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
            var result = builder.WithProductionVersioning();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

            Assert.True(options.EnableAutomaticConversion);
            Assert.True(options.StrictValidation);
            Assert.Equal(VersionCompatibilityMode.Strict, options.DefaultCompatibilityMode);
            Assert.False(options.LogVersioningActivity);
            Assert.Equal(TimeSpan.FromSeconds(30), options.ConversionTimeout);
        }

        [Fact]
        public void WithBackwardCompatibleVersioning_ConfiguresForBackwardCompatibility()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithBackwardCompatibleVersioning();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

            Assert.True(options.EnableAutomaticConversion);
            Assert.Equal(VersionCompatibilityMode.Backward, options.DefaultCompatibilityMode);
            Assert.True(options.AllowVersionDowngrades);
            Assert.False(options.StrictValidation);
        }

        [Fact]
        public void WithForwardCompatibleVersioning_ConfiguresForForwardCompatibility()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);

            // Act
            var result = builder.WithForwardCompatibleVersioning();

            // Assert
            Assert.Same(builder, result);

            var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<MessageVersioningOptions>();

            Assert.True(options.EnableAutomaticConversion);
            Assert.Equal(VersionCompatibilityMode.Forward, options.DefaultCompatibilityMode);
            Assert.True(options.IgnoreUnknownProperties);
            Assert.False(options.StrictValidation);
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
        public void MessageVersioningOptions_RegisterConverter_DoesNotThrow()
        {
            // Arrange
            var options = new MessageVersioningOptions();
            var converter = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act & Assert - Should not throw
            options.RegisterConverter(converter);
        }

        [Fact]
        public void MessageVersioningOptions_RegisterMultipleConverters_DoesNotThrow()
        {
            // Arrange
            var options = new MessageVersioningOptions();
            var converter1 = new Mock<IMessageConverter<TestMessage>>().Object;
            var converter2 = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act & Assert - Should not throw
            options.RegisterConverter(converter1);
            options.RegisterConverter(converter2);
        }

        #endregion

        #region VersionCompatibilityMode Enum Tests

        [Fact]
        public void VersionCompatibilityMode_HasExpectedValues()
        {
            // Assert
            Assert.Equal(0, (int)VersionCompatibilityMode.Strict);
            Assert.Equal(1, (int)VersionCompatibilityMode.Backward);
            Assert.Equal(2, (int)VersionCompatibilityMode.Forward);
            Assert.Equal(3, (int)VersionCompatibilityMode.Flexible);
        }

        #endregion

        #region MessageVersioningProfiles Tests

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
        public void MessageVersioningProfiles_AllProfiles_ReturnNewInstances()
        {
            // Act
            var profile1 = MessageVersioningProfiles.Microservices;
            var profile2 = MessageVersioningProfiles.Microservices;

            // Assert - Each access should return a new instance
            Assert.NotSame(profile1, profile2);
        }

        #endregion

        #region Chaining Tests

        [Fact]
        public void WithVersioning_CanBeChainedWithOtherMethods()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);
            var converter = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act
            var result = builder
                .WithVersioning()
                .RegisterConverter(converter)
                .WithDevelopmentVersioning();

            // Assert
            Assert.Same(builder, result);
        }

        [Fact]
        public void RegisterConverter_CanBeChainedWithOtherMethods()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = new HeroMessagingBuilder(services);
            var converter1 = new Mock<IMessageConverter<TestMessage>>().Object;
            var converter2 = new Mock<IMessageConverter<TestMessage>>().Object;

            // Act
            var result = builder
                .RegisterConverter(converter1)
                .RegisterConverter(converter2)
                .WithVersioning();

            // Assert
            Assert.Same(builder, result);
        }

        #endregion

        #region Helper Classes

        public sealed class TestMessage : IMessage
        {
            public Guid MessageId { get; set; } = Guid.NewGuid();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string? CorrelationId { get; set; }
            public string? CausationId { get; set; }
            public Dictionary<string, object>? Metadata { get; set; }
        }

        #endregion
    }
}
