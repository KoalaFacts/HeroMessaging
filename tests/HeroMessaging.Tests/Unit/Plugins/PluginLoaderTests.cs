using HeroMessaging.Abstractions.Plugins;
using HeroMessaging.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Plugins
{
    [Trait("Category", "Unit")]
    public sealed class PluginLoaderTests
    {
        private readonly Mock<ILogger<PluginLoader>> _loggerMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;

        public PluginLoaderTests()
        {
            _loggerMock = new Mock<ILogger<PluginLoader>>();
            _serviceProviderMock = new Mock<IServiceProvider>();
        }

        private PluginLoader CreateLoader()
        {
            return new PluginLoader(_loggerMock.Object);
        }

        private IPluginDescriptor CreateValidDescriptor(Type pluginType)
        {
            return new PluginDescriptor
            {
                Name = "TestPlugin",
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = pluginType
            };
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            var loader = new PluginLoader(null);

            // Assert
            Assert.NotNull(loader);
        }

        [Fact]
        public void Constructor_WithLogger_CreatesInstance()
        {
            // Act
            var loader = CreateLoader();

            // Assert
            Assert.NotNull(loader);
        }

        #endregion

        #region LoadAsync Tests

        [Fact]
        public async Task LoadAsync_WithNullDescriptor_ThrowsArgumentNullException()
        {
            // Arrange
            var loader = CreateLoader();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                loader.LoadAsync(null!, _serviceProviderMock.Object, TestContext.Current.CancellationToken));

            Assert.Equal("descriptor", ex.ParamName);
        }

        [Fact]
        public async Task LoadAsync_WithNullServiceProvider_ThrowsArgumentNullException()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(TestPlugin));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                loader.LoadAsync(descriptor, null!, TestContext.Current.CancellationToken));

            Assert.Equal("serviceProvider", ex.ParamName);
        }

        [Fact]
        public async Task LoadAsync_WithInvalidPluginType_ThrowsInvalidOperationException()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(InvalidPlugin)); // Doesn't implement IMessagingPlugin

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                loader.LoadAsync(descriptor, _serviceProviderMock.Object, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task LoadAsync_WithValidPlugin_ReturnsPluginInstance()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(TestPlugin));
            var pluginInstance = new TestPlugin();

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(TestPlugin)))
                .Returns(pluginInstance);

            // Act
            var result = await loader.LoadAsync(descriptor, _serviceProviderMock.Object, TestContext.Current.CancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.Same(pluginInstance, result);
        }

        [Fact]
        public async Task LoadAsync_WithConfigureAction_AppliesConfiguration()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(TestPlugin));
            var pluginInstance = new TestPlugin();
            var configureWasCalled = false;

            _serviceProviderMock
                .Setup(sp => sp.GetService(typeof(TestPlugin)))
                .Returns(pluginInstance);

            // Act
            await loader.LoadAsync(descriptor, _serviceProviderMock.Object, plugin =>
            {
                configureWasCalled = true;
                Assert.Same(pluginInstance, plugin, TestContext.Current.CancellationToken);
            });

            // Assert
            Assert.True(configureWasCalled);
        }

        #endregion

        #region CanLoadAsync Tests

        [Fact]
        public async Task CanLoadAsync_WithNullDescriptor_ReturnsFalse()
        {
            // Arrange
            var loader = CreateLoader();

            // Act
            var result = await loader.CanLoadAsync(null!, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanLoadAsync_WithAbstractType_ReturnsFalse()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(AbstractPlugin));

            // Act
            var result = await loader.CanLoadAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanLoadAsync_WithInterfaceType_ReturnsFalse()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(IMessagingPlugin));

            // Act
            var result = await loader.CanLoadAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanLoadAsync_WithNonPluginType_ReturnsFalse()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(InvalidPlugin));

            // Act
            var result = await loader.CanLoadAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CanLoadAsync_WithValidPluginType_ReturnsTrue()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(TestPlugin));

            // Act
            var result = await loader.CanLoadAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result);
        }

        #endregion

        #region ValidateAsync Tests

        [Fact]
        public async Task ValidateAsync_WithNullDescriptor_ReturnsInvalidResult()
        {
            // Arrange
            var loader = CreateLoader();

            // Act
            var result = await loader.ValidateAsync(null!, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Plugin descriptor is null", result.Errors);
        }

        [Fact]
        public async Task ValidateAsync_WithNullPluginType_ReturnsInvalidResult()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = new PluginDescriptor
            {
                Name = "TestPlugin",
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = null!
            };

            // Act
            var result = await loader.ValidateAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Plugin type is null", result.Errors);
        }

        [Fact]
        public async Task ValidateAsync_WithNonPluginType_ReturnsInvalidResult()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(InvalidPlugin));

            // Act
            var result = await loader.ValidateAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("does not implement IMessagingPlugin"));
        }

        [Fact]
        public async Task ValidateAsync_WithEmptyName_ReturnsInvalidResult()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = new PluginDescriptor
            {
                Name = string.Empty,
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = typeof(TestPlugin)
            };

            // Act
            var result = await loader.ValidateAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Plugin name is empty", result.Errors);
        }

        [Fact]
        public async Task ValidateAsync_WithMissingDescription_ReturnsWarning()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = CreateValidDescriptor(typeof(TestPlugin));

            // Act
            var result = await loader.ValidateAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.Contains("Plugin has no description", result.Warnings);
        }

        [Fact]
        public async Task ValidateAsync_WithValidPlugin_ReturnsValidResult()
        {
            // Arrange
            var loader = CreateLoader();
            var descriptor = new PluginDescriptor
            {
                Name = "TestPlugin",
                Version = new Version(1, 0, 0),
                Category = PluginCategory.Custom,
                PluginType = typeof(TestPlugin),
                Description = "Test plugin description"
            };

            // Act
            var result = await loader.ValidateAsync(descriptor, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        #endregion

        #region Test Helper Classes

        public class TestPlugin : IMessagingPlugin
        {
            public string Name => "TestPlugin";

            public void Configure(IServiceCollection services)
            {
            }

            public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task ShutdownAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        public abstract class AbstractPlugin : IMessagingPlugin
        {
            public abstract string Name { get; }

            public abstract void Configure(IServiceCollection services);
            public abstract Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);
            public abstract Task ShutdownAsync(CancellationToken cancellationToken = default);
        }

        public class InvalidPlugin
        {
            // Does not implement IMessagingPlugin
        }

        #endregion
    }
}
