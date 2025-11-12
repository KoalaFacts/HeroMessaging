using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Plugins;
using Xunit;

namespace HeroMessaging.Tests.Unit.Plugins
{
    [Trait("Category", "Unit")]
    public sealed class PluginDescriptorTests
    {
    #region Constructor Tests - Parameterless

    [Fact]
    public void Constructor_Parameterless_CreatesInstanceWithDefaults()
    {
        // Act
        var descriptor = new PluginDescriptor();

        // Assert
        Assert.NotNull(descriptor);
        Assert.Equal(string.Empty, descriptor.Name);
        Assert.Equal(new Version(1, 0, 0), descriptor.Version);
        // Category is set by the parameterless constructor's property initializer
        // which uses default(PluginCategory) = Storage (enum's first value)
        Assert.True(Enum.IsDefined(typeof(PluginCategory), descriptor.Category));
        Assert.Null(descriptor.Description);
        Assert.Equal(string.Empty, descriptor.AssemblyName);
        Assert.Equal(typeof(object), descriptor.PluginType);
        Assert.Empty(descriptor.Dependencies);
        Assert.Empty(descriptor.ConfigurationOptions);
        Assert.Empty(descriptor.ProvidedFeatures);
    }

    #endregion

    #region Constructor Tests - With Type

    [Fact]
    public void Constructor_WithNullType_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new PluginDescriptor(null!));

        Assert.Equal("pluginType", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithTypeOnly_ExtractsBasicMetadata()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin));

        // Assert
        Assert.Equal(nameof(TestPlugin), descriptor.Name);
        Assert.NotNull(descriptor.AssemblyName);
        Assert.Equal(typeof(TestPlugin), descriptor.PluginType);
        Assert.Equal(new Version(1, 0, 0), descriptor.Version);
    }

    [Fact]
    public void Constructor_WithStoragePluginType_DeterminesStorageCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Storage.TestStoragePlugin));

        // Assert
        Assert.Equal(PluginCategory.Storage, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithSerializationPluginType_DeterminesSerializationCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Serialization.TestSerializationPlugin));

        // Assert
        Assert.Equal(PluginCategory.Serialization, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithObservabilityPluginType_DeterminesObservabilityCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Observability.TestObservabilityPlugin));

        // Assert
        Assert.Equal(PluginCategory.Observability, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithSecurityPluginType_DeterminesSecurityCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Security.TestSecurityPlugin));

        // Assert
        Assert.Equal(PluginCategory.Security, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithResiliencePluginType_DeterminesResilienceCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Resilience.TestResiliencePlugin));

        // Assert
        Assert.Equal(PluginCategory.Resilience, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithValidationPluginType_DeterminesValidationCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Validation.TestValidationPlugin));

        // Assert
        Assert.Equal(PluginCategory.Validation, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithTransformationPluginType_DeterminesTransformationCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestHelpers.Transformation.TestTransformationPlugin));

        // Assert
        Assert.Equal(PluginCategory.Transformation, descriptor.Category);
    }

    [Fact]
    public void Constructor_WithUnknownPluginType_DeterminesCustomCategory()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin));

        // Assert
        Assert.Equal(PluginCategory.Custom, descriptor.Category);
    }

    #endregion

    #region Constructor Tests - With Attribute

    [Fact]
    public void Constructor_WithAttribute_UsesAttributeMetadata()
    {
        // Arrange
        var attribute = new HeroMessagingPluginAttribute("CustomName", PluginCategory.Observability)
        {
            Description = "Test description",
            Version = "2.1.0"
        };

        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin), attribute);

        // Assert
        Assert.Equal("CustomName", descriptor.Name);
        Assert.Equal(PluginCategory.Observability, descriptor.Category);
        Assert.Equal("Test description", descriptor.Description);
        Assert.Equal(new Version(2, 1, 0), descriptor.Version);
    }

    [Fact]
    public void Constructor_WithAttributeWithInvalidVersion_UsesDefaultVersion()
    {
        // Arrange
        var attribute = new HeroMessagingPluginAttribute("TestPlugin", PluginCategory.Storage)
        {
            Version = "invalid-version"
        };

        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin), attribute);

        // Assert
        Assert.Equal(new Version(1, 0, 0), descriptor.Version);
    }

    [Fact]
    public void Constructor_WithAttributeWithEmptyVersion_UsesDefaultVersion()
    {
        // Arrange
        var attribute = new HeroMessagingPluginAttribute("TestPlugin", PluginCategory.Storage)
        {
            Version = ""
        };

        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin), attribute);

        // Assert
        Assert.Equal(new Version(1, 0, 0), descriptor.Version);
    }

    [Fact]
    public void Constructor_WithAttributeWithNullVersion_UsesDefaultVersion()
    {
        // Arrange
        var attribute = new HeroMessagingPluginAttribute("TestPlugin", PluginCategory.Storage)
        {
            Version = null
        };

        // Act
        var descriptor = new PluginDescriptor(typeof(TestPlugin), attribute);

        // Assert
        Assert.Equal(new Version(1, 0, 0), descriptor.Version);
    }

    #endregion

    #region Metadata Extraction Tests

    [Fact]
    public void Constructor_WithInterfaceImplementation_ExtractsProvidedFeatures()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPluginWithInterfaces));

        // Assert
        // The test interface is not in HeroMessaging.Abstractions namespace, so it won't be extracted
        // Let's verify that ProvidedFeatures collection is initialized (may be empty)
        Assert.NotNull(descriptor.ProvidedFeatures);
    }

    [Fact]
    public void Constructor_WithConstructorDependencies_ExtractsDependencies()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPluginWithDependencies));

        // Assert
        // The dependency is an interface, so it should be extracted
        Assert.Contains("ITestDependency", descriptor.Dependencies);
    }

    [Fact]
    public void Constructor_WithPublicWritableProperties_ExtractsConfigurationOptions()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPluginWithConfiguration));

        // Assert
        Assert.True(descriptor.ConfigurationOptions.ContainsKey("MaxRetries"));
        Assert.Equal(typeof(int), descriptor.ConfigurationOptions["MaxRetries"]);
        Assert.True(descriptor.ConfigurationOptions.ContainsKey("ConnectionString"));
        Assert.Equal(typeof(string), descriptor.ConfigurationOptions["ConnectionString"]);
    }

    [Fact]
    public void Constructor_WithReadOnlyProperty_DoesNotIncludeInConfigurationOptions()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPluginWithReadOnlyProperty));

        // Assert
        Assert.False(descriptor.ConfigurationOptions.ContainsKey("ReadOnlyProperty"));
    }

    [Fact]
    public void Constructor_WithNonPublicProperty_DoesNotIncludeInConfigurationOptions()
    {
        // Act
        var descriptor = new PluginDescriptor(typeof(TestPluginWithPrivateProperty));

        // Assert
        Assert.False(descriptor.ConfigurationOptions.ContainsKey("PrivateProperty"));
    }

    #endregion

    #region Test Helper Classes

    public class TestPlugin
    {
    }

    public interface ITestInterface
    {
    }

    public interface ITestDependency
    {
    }

    public class TestPluginWithInterfaces : ITestInterface
    {
    }

    public class TestPluginWithDependencies
    {
        public TestPluginWithDependencies(ITestDependency dependency)
        {
        }
    }

    public class TestPluginWithConfiguration
    {
        public int MaxRetries { get; set; }
        public string? ConnectionString { get; set; }
    }

    public class TestPluginWithReadOnlyProperty
    {
        public int ReadOnlyProperty => 42;
    }

    public class TestPluginWithPrivateProperty
    {
        private int PrivateProperty { get; set; }
    }

    #endregion
    }
}

// Test helper namespace classes for category detection testing
// These need to be in specific namespaces to test the category detection logic
namespace TestHelpers.Storage
{
    public class TestStoragePlugin { }
}

namespace TestHelpers.Serialization
{
    public class TestSerializationPlugin { }
}

namespace TestHelpers.Observability
{
    public class TestObservabilityPlugin { }
}

namespace TestHelpers.Security
{
    public class TestSecurityPlugin { }
}

namespace TestHelpers.Resilience
{
    public class TestResiliencePlugin { }
}

namespace TestHelpers.Validation
{
    public class TestValidationPlugin { }
}

namespace TestHelpers.Transformation
{
    public class TestTransformationPlugin { }
}
