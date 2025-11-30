using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Plugins;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Plugins
{
    [Trait("Category", "Unit")]
    public sealed class PluginRegistryTests
    {
        private readonly Mock<ILogger<PluginRegistry>> _loggerMock;

        public PluginRegistryTests()
        {
            _loggerMock = new Mock<ILogger<PluginRegistry>>();
        }

        private PluginRegistry CreateRegistry()
        {
            return new PluginRegistry(_loggerMock.Object);
        }

        private IPluginDescriptor CreateTestDescriptor(string name = "TestPlugin", PluginCategory category = PluginCategory.Custom)
        {
            return new PluginDescriptor
            {
                Name = name,
                Version = new Version(1, 0, 0),
                Category = category,
                PluginType = typeof(object)
            };
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            var registry = new PluginRegistry(null);

            // Assert
            Assert.NotNull(registry);
        }

        [Fact]
        public void Constructor_WithLogger_CreatesInstance()
        {
            // Act
            var registry = CreateRegistry();

            // Assert
            Assert.NotNull(registry);
        }

        #endregion

        #region Register Tests

        [Fact]
        public void Register_WithNullDescriptor_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                registry.Register(null!));

            Assert.Equal("descriptor", ex.ParamName);
        }

        [Fact]
        public void Register_WithValidDescriptor_AddsToRegistry()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptor = CreateTestDescriptor();

            // Act
            registry.Register(descriptor);

            // Assert
            Assert.True(registry.IsRegistered(descriptor.Name));
        }

        [Fact]
        public void Register_WithDuplicateName_DoesNotReplace()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptor1 = CreateTestDescriptor("Plugin1");
            var descriptor2 = CreateTestDescriptor("Plugin1"); // Same name

            // Act
            registry.Register(descriptor1);
            registry.Register(descriptor2); // Should not replace

            // Assert
            var registered = registry.GetByName("Plugin1");
            Assert.Same(descriptor1, registered);
        }

        #endregion

        #region RegisterRange Tests

        [Fact]
        public void RegisterRange_WithNullDescriptors_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                registry.RegisterRange(null!));

            Assert.Equal("descriptors", ex.ParamName);
        }

        [Fact]
        public void RegisterRange_WithEmptyList_DoesNotThrow()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            registry.RegisterRange([]);

            // Assert
            Assert.Empty(registry.GetAll());
        }

        [Fact]
        public void RegisterRange_WithMultipleDescriptors_AddsAll()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptors = new[]
            {
                CreateTestDescriptor("Plugin1"),
                CreateTestDescriptor("Plugin2"),
                CreateTestDescriptor("Plugin3")
            };

            // Act
            registry.RegisterRange(descriptors);

            // Assert
            Assert.Equal(3, registry.GetAll().Count());
        }

        #endregion

        #region GetAll Tests

        [Fact]
        public void GetAll_WithEmptyRegistry_ReturnsEmptyList()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetAll();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAll_WithRegisteredPlugins_ReturnsAllPlugins()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("Plugin1"));
            registry.Register(CreateTestDescriptor("Plugin2"));

            // Act
            var result = registry.GetAll();

            // Assert
            Assert.Equal(2, result.Count());
        }

        #endregion

        #region GetByCategory Tests

        [Fact]
        public void GetByCategory_WithNoMatchingPlugins_ReturnsEmptyList()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("Plugin1", PluginCategory.Storage));

            // Act
            var result = registry.GetByCategory(PluginCategory.Serialization);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetByCategory_WithMatchingPlugins_ReturnsFilteredList()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("Storage1", PluginCategory.Storage));
            registry.Register(CreateTestDescriptor("Storage2", PluginCategory.Storage));
            registry.Register(CreateTestDescriptor("Serialization1", PluginCategory.Serialization));

            // Act
            var result = registry.GetByCategory(PluginCategory.Storage);

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, p => Assert.Equal(PluginCategory.Storage, p.Category));
        }

        #endregion

        #region GetByName Tests

        [Fact]
        public void GetByName_WithNullName_ReturnsNull()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetByName(null!);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetByName_WithEmptyName_ReturnsNull()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetByName(string.Empty);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetByName_WithNonExistentName_ReturnsNull()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetByName("NonExistent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetByName_WithValidName_ReturnsDescriptor()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptor = CreateTestDescriptor("TestPlugin");
            registry.Register(descriptor);

            // Act
            var result = registry.GetByName("TestPlugin");

            // Assert
            Assert.NotNull(result);
            Assert.Same(descriptor, result);
        }

        #endregion

        #region IsRegistered Tests

        [Fact]
        public void IsRegistered_WithNullName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.IsRegistered(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRegistered_WithEmptyName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.IsRegistered(string.Empty);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRegistered_WithNonExistentName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.IsRegistered("NonExistent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRegistered_WithRegisteredName_ReturnsTrue()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("TestPlugin"));

            // Act
            var result = registry.IsRegistered("TestPlugin");

            // Assert
            Assert.True(result);
        }

        #endregion

        #region Unregister Tests

        [Fact]
        public void Unregister_WithNullName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.Unregister(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Unregister_WithEmptyName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.Unregister(string.Empty);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Unregister_WithNonExistentName_ReturnsFalse()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.Unregister("NonExistent");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Unregister_WithValidName_RemovesAndReturnsTrue()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("TestPlugin"));

            // Act
            var result = registry.Unregister("TestPlugin");

            // Assert
            Assert.True(result);
            Assert.False(registry.IsRegistered("TestPlugin"));
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_WithEmptyRegistry_DoesNotThrow()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            registry.Clear();

            // Assert
            Assert.Empty(registry.GetAll());
        }

        [Fact]
        public void Clear_WithRegisteredPlugins_RemovesAll()
        {
            // Arrange
            var registry = CreateRegistry();
            registry.Register(CreateTestDescriptor("Plugin1"));
            registry.Register(CreateTestDescriptor("Plugin2"));

            // Act
            registry.Clear();

            // Assert
            Assert.Empty(registry.GetAll());
        }

        #endregion

        #region GetByFeature Tests

        [Fact]
        public void GetByFeature_WithNullFeature_ReturnsEmptyList()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetByFeature(null!);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetByFeature_WithEmptyFeature_ReturnsEmptyList()
        {
            // Arrange
            var registry = CreateRegistry();

            // Act
            var result = registry.GetByFeature(string.Empty);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetByFeature_WithNoMatchingPlugins_ReturnsEmptyList()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptor = CreateTestDescriptor("Plugin1");
            registry.Register(descriptor);

            // Act
            var result = registry.GetByFeature("NonExistentFeature");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetByFeature_WithMatchingPlugins_ReturnsFilteredList()
        {
            // Arrange
            var registry = CreateRegistry();
            var descriptor = new PluginDescriptor
            {
                Name = "Plugin1",
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = typeof(object),
                ProvidedFeatures = new List<string> { "Feature1", "Feature2" }
            };
            registry.Register(descriptor);

            // Act
            var result = registry.GetByFeature("Feature1");

            // Assert
            Assert.Single(result);
            Assert.Contains(descriptor, result);
        }

        #endregion
    }
}
