using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Plugins
{
    [Trait("Category", "Unit")]
    public sealed class PluginDiscoveryServiceTests
    {
        private readonly Mock<IPluginDiscovery> _discoveryMock;
        private readonly Mock<IPluginRegistry> _registryMock;
        private readonly Mock<IPluginLoader> _loaderMock;
        private readonly IServiceCollection _services;
        private readonly Mock<ILogger<PluginDiscoveryService>> _loggerMock;

        public PluginDiscoveryServiceTests()
        {
            _discoveryMock = new Mock<IPluginDiscovery>();
            _registryMock = new Mock<IPluginRegistry>();
            _loaderMock = new Mock<IPluginLoader>();
            _services = new ServiceCollection();
            _loggerMock = new Mock<ILogger<PluginDiscoveryService>>();
        }

        private PluginDiscoveryService CreateService()
        {
            return new PluginDiscoveryService(
                _discoveryMock.Object,
                _registryMock.Object,
                _loaderMock.Object,
                _services,
                _loggerMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullDiscovery_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new PluginDiscoveryService(null!, _registryMock.Object, _loaderMock.Object, _services));

            Assert.Equal("discovery", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullRegistry_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new PluginDiscoveryService(_discoveryMock.Object, null!, _loaderMock.Object, _services));

            Assert.Equal("registry", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullLoader_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new PluginDiscoveryService(_discoveryMock.Object, _registryMock.Object, null!, _services));

            Assert.Equal("loader", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullServices_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new PluginDiscoveryService(_discoveryMock.Object, _registryMock.Object, _loaderMock.Object, null!));

            Assert.Equal("services", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            var service = new PluginDiscoveryService(
                _discoveryMock.Object,
                _registryMock.Object,
                _loaderMock.Object,
                _services,
                null);

            // Assert
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_WithValidArguments_CreatesInstance()
        {
            // Act
            var service = CreateService();

            // Assert
            Assert.NotNull(service);
        }

        #endregion

        #region DiscoverAndRegisterPluginsAsync Tests

        [Fact]
        public async Task DiscoverAndRegisterPluginsAsync_WithNoPlugins_CompletesSuccessfully()
        {
            // Arrange
            var service = CreateService();
            _discoveryMock
                .Setup(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Act
            await service.DiscoverAndRegisterPluginsAsync();

            // Assert
            _discoveryMock.Verify(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()), Times.Once);
            _registryMock.Verify(r => r.RegisterRange(It.IsAny<IEnumerable<IPluginDescriptor>>()), Times.Once);
        }

        [Fact]
        public async Task DiscoverAndRegisterPluginsAsync_WithPlugins_RegistersAllPlugins()
        {
            // Arrange
            var service = CreateService();
            var plugins = new List<IPluginDescriptor>
            {
                CreateTestDescriptor("Plugin1"),
                CreateTestDescriptor("Plugin2"),
                CreateTestDescriptor("Plugin3")
            };

            _discoveryMock
                .Setup(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(plugins);

            // Act
            await service.DiscoverAndRegisterPluginsAsync();

            // Assert
            _registryMock.Verify(r => r.RegisterRange(plugins), Times.Once);
        }

        [Fact]
        public async Task DiscoverAndRegisterPluginsAsync_WithPlugins_RegistersPluginTypesInDI()
        {
            // Arrange
            var service = CreateService();
            var plugin = CreateTestDescriptor("TestPlugin");
            var plugins = new List<IPluginDescriptor> { plugin };

            _discoveryMock
                .Setup(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(plugins);

            // Act
            await service.DiscoverAndRegisterPluginsAsync();

            // Assert
            Assert.Contains(_services, sd => sd.ServiceType == plugin.PluginType);
        }

        [Fact]
        public async Task DiscoverAndRegisterPluginsAsync_WhenDiscoveryThrows_DoesNotThrow()
        {
            // Arrange
            var service = CreateService();
            _discoveryMock
                .Setup(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Discovery failed"));

            // Act & Assert - Should not throw
            await service.DiscoverAndRegisterPluginsAsync();
        }

        [Fact]
        public async Task DiscoverAndRegisterPluginsAsync_WhenRegistrationThrows_ContinuesWithOtherPlugins()
        {
            // Arrange
            var service = CreateService();
            var plugins = new List<IPluginDescriptor>
            {
                CreateTestDescriptor("Plugin1"),
                CreateTestDescriptor("Plugin2")
            };

            _discoveryMock
                .Setup(d => d.DiscoverPluginsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(plugins);

            // Act - Should handle exceptions gracefully
            await service.DiscoverAndRegisterPluginsAsync();

            // Assert - Both plugins should be attempted
            _registryMock.Verify(r => r.RegisterRange(plugins), Times.Once);
        }

        #endregion

        #region Helper Methods

        private IPluginDescriptor CreateTestDescriptor(string name)
        {
            return new PluginDescriptor
            {
                Name = name,
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = typeof(TestPlugin)
            };
        }

        public class TestPlugin
        {
        }

        #endregion
    }
}
