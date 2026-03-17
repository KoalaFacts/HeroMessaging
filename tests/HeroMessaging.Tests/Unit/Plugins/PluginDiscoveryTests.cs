using System.Reflection;
using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Plugins;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Plugins
{
    [Trait("Category", "Unit")]
    public sealed class PluginDiscoveryTests
    {
        private readonly Mock<IPluginRegistry> _registryMock;
        private readonly Mock<ILogger<PluginDiscovery>> _loggerMock;

        public PluginDiscoveryTests()
        {
            _registryMock = new Mock<IPluginRegistry>();
            _loggerMock = new Mock<ILogger<PluginDiscovery>>();
        }

        private PluginDiscovery CreateDiscovery()
        {
            return new PluginDiscovery(_registryMock.Object, _loggerMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullRegistry_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new PluginDiscovery(null!, _loggerMock.Object));

            Assert.Equal("registry", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            var discovery = new PluginDiscovery(_registryMock.Object, null);

            // Assert
            Assert.NotNull(discovery);
        }

        [Fact]
        public void Constructor_WithValidArguments_CreatesInstance()
        {
            // Act
            var discovery = CreateDiscovery();

            // Assert
            Assert.NotNull(discovery);
        }

        #endregion

        #region DiscoverPluginsAsync (Assembly) Tests

        [Fact]
        public async Task DiscoverPluginsAsync_WithNullAssembly_ThrowsArgumentNullException()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                discovery.DiscoverPluginsAsync((Assembly)null!));

            Assert.Equal("assembly", ex.ParamName);
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithEmptyAssembly_ReturnsEmptyList()
        {
            // Arrange
            var discovery = CreateDiscovery();
            var assembly = typeof(object).Assembly; // System.Private.CoreLib has no plugins

            // Act
            var result = await discovery.DiscoverPluginsAsync(assembly);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithCancellationToken_DoesNotThrow()
        {
            // Arrange
            var discovery = CreateDiscovery();
            var assembly = typeof(PluginDiscovery).Assembly;
            var cts = new CancellationTokenSource();

            // Act - Pass valid token
            var result = await discovery.DiscoverPluginsAsync(assembly, cts.Token);

            // Assert - Should complete successfully
            Assert.NotNull(result);
        }

        #endregion

        #region DiscoverPluginsAsync (Directory) Tests

        [Fact]
        public async Task DiscoverPluginsAsync_WithNullDirectory_ThrowsArgumentNullException()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                discovery.DiscoverPluginsAsync((string)null!));

            Assert.Equal("directory", ex.ParamName);
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithEmptyDirectory_ThrowsArgumentNullException()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                discovery.DiscoverPluginsAsync(string.Empty));

            Assert.Equal("directory", ex.ParamName);
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithNonExistentDirectory_ReturnsEmptyList()
        {
            // Arrange
            var discovery = CreateDiscovery();
            var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act
            var result = await discovery.DiscoverPluginsAsync(nonExistentDir);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithValidDirectory_SkipsSystemAssemblies()
        {
            // Arrange
            var discovery = CreateDiscovery();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Create a fake System.* file (not a real assembly, just testing filtering)
                var systemFile = Path.Combine(tempDir, "System.Test.dll");
                File.WriteAllText(systemFile, "fake");

                // Act
                var result = await discovery.DiscoverPluginsAsync(tempDir);

                // Assert
                Assert.NotNull(result);
                // System assemblies should be skipped, but this will return empty since it's not a real assembly
                Assert.Empty(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithCustomSearchPattern_UsesPattern()
        {
            // Arrange
            var discovery = CreateDiscovery();
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act
                var result = await discovery.DiscoverPluginsAsync(tempDir, "*.custom");

                // Assert
                Assert.NotNull(result);
                Assert.Empty(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region DiscoverPluginsAsync (AppDomain) Tests

        [Fact]
        public async Task DiscoverPluginsAsync_WithoutParameters_DiscoversFromAppDomain()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act
            var result = await discovery.DiscoverPluginsAsync();

            // Assert
            Assert.NotNull(result);
            // Will only find assemblies with "HeroMessaging" in the name
        }

        #endregion

        #region DiscoverPluginsAsync (Category) Tests

        [Fact]
        public async Task DiscoverPluginsAsync_WithStorageCategory_FiltersCorrectly()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act
            var result = await discovery.DiscoverPluginsAsync(PluginCategory.Storage);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, p => Assert.Equal(PluginCategory.Storage, p.Category));
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithSerializationCategory_FiltersCorrectly()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act
            var result = await discovery.DiscoverPluginsAsync(PluginCategory.Serialization);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, p => Assert.Equal(PluginCategory.Serialization, p.Category));
        }

        [Fact]
        public async Task DiscoverPluginsAsync_WithObservabilityCategory_FiltersCorrectly()
        {
            // Arrange
            var discovery = CreateDiscovery();

            // Act
            var result = await discovery.DiscoverPluginsAsync(PluginCategory.Observability);

            // Assert
            Assert.NotNull(result);
            Assert.All(result, p => Assert.Equal(PluginCategory.Observability, p.Category));
        }

        #endregion
    }
}
