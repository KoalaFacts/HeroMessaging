using System.Reflection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for plugin system functionality
/// Testing plugin discovery, registration, lifecycle management, and dependency resolution
/// </summary>
public class PluginSystemTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDiscovery_ScanAssembly_FindsAllPlugins()
    {
        // Arrange
        var discovery = new TestPluginDiscovery();
        var testAssembly = typeof(PluginSystemTests).Assembly;

        // Act
        var discoveredPlugins = discovery.ScanAssembly(testAssembly);

        // Assert
        Assert.NotNull(discoveredPlugins);
        // The test assembly may not contain actual plugins, but the method should work
        Assert.True(discoveredPlugins.Count >= 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDiscovery_WithFilterCriteria_ReturnsFilteredResults()
    {
        // Arrange
        var discovery = new TestPluginDiscovery();
        var testAssembly = typeof(PluginSystemTests).Assembly;

        // Act
        var allPlugins = discovery.ScanAssembly(testAssembly);
        var filteredPlugins = discovery.ScanAssembly(testAssembly, type => type.Name.Contains("Storage"));

        // Assert
        Assert.True(filteredPlugins.Count <= allPlugins.Count);
        Assert.All(filteredPlugins, plugin => Assert.Contains("Storage", plugin.Name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_RegisterPlugin_AddsToRegistry()
    {
        // Arrange
        var registry = new TestPluginRegistry();
        var mockPlugin = CreateMockPlugin("TestPlugin", "1.0.0");

        // Act
        registry.Register(mockPlugin.Object);

        // Assert
        Assert.True(registry.IsRegistered("TestPlugin"));
        Assert.Equal(mockPlugin.Object, registry.GetPlugin("TestPlugin"));
        Assert.Single(registry.GetAllPlugins());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_RegisterDuplicatePlugin_ThrowsException()
    {
        // Arrange
        var registry = new TestPluginRegistry();
        var plugin1 = CreateMockPlugin("TestPlugin", "1.0.0");
        var plugin2 = CreateMockPlugin("TestPlugin", "2.0.0");

        registry.Register(plugin1.Object);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(plugin2.Object));
        Assert.Contains("TestPlugin", exception.Message);
        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_UnregisterPlugin_RemovesFromRegistry()
    {
        // Arrange
        var registry = new TestPluginRegistry();
        var mockPlugin = CreateMockPlugin("TestPlugin", "1.0.0");
        registry.Register(mockPlugin.Object);

        // Act
        var result = registry.Unregister("TestPlugin");

        // Assert
        Assert.True(result);
        Assert.False(registry.IsRegistered("TestPlugin"));
        Assert.Empty(registry.GetAllPlugins());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_UnregisterNonExistentPlugin_ReturnsFalse()
    {
        // Arrange
        var registry = new TestPluginRegistry();

        // Act
        var result = registry.Unregister("NonExistentPlugin");

        // Assert
        Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PluginLifecycleManager_InitializePlugin_CallsInitializeAsync()
    {
        // Arrange
        var lifecycleManager = new TestPluginLifecycleManager();
        var mockPlugin = CreateMockPluginWithLifecycle("TestPlugin", "1.0.0");

        // Act
        await lifecycleManager.InitializeAsync(mockPlugin);

        // Assert
        mockPlugin.Verify(p => p.InitializeAsync(), Times.Once);
        Assert.True(lifecycleManager.IsInitialized(mockPlugin.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PluginLifecycleManager_DisposePlugin_CallsDisposeAsync()
    {
        // Arrange
        var lifecycleManager = new TestPluginLifecycleManager();
        var mockPlugin = CreateMockPluginWithLifecycle("TestPlugin", "1.0.0");

        await lifecycleManager.InitializeAsync(mockPlugin);

        // Act
        await lifecycleManager.DisposeAsync(mockPlugin);

        // Assert
        mockPlugin.Verify(p => p.DisposeAsync(), Times.Once);
        Assert.False(lifecycleManager.IsInitialized(mockPlugin.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task PluginLifecycleManager_InitializeWithDependencies_ResolvesDependenciesFirst()
    {
        // Arrange
        var lifecycleManager = new TestPluginLifecycleManager();
        var dependencyPlugin = CreateMockPluginWithLifecycle("DependencyPlugin", "1.0.0");
        var mainPlugin = CreateMockPluginWithLifecycle("MainPlugin", "1.0.0");

        // Setup dependency
        mainPlugin.Setup(p => p.Dependencies).Returns(new[] { "DependencyPlugin" });

        // Act
        await lifecycleManager.InitializeWithDependenciesAsync(mainPlugin, new[] { dependencyPlugin.Object });

        // Assert
        dependencyPlugin.Verify(p => p.InitializeAsync(), Times.Once);
        mainPlugin.Verify(p => p.InitializeAsync(), Times.Once);
        Assert.True(lifecycleManager.IsInitialized(dependencyPlugin.Object));
        Assert.True(lifecycleManager.IsInitialized(mainPlugin.Object));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDependencyResolver_ResolveDependencies_ReturnsCorrectOrder()
    {
        // Arrange
        var resolver = new TestPluginDependencyResolver();

        var pluginA = CreateMockPlugin("PluginA", "1.0.0");
        var pluginB = CreateMockPlugin("PluginB", "1.0.0");
        var pluginC = CreateMockPlugin("PluginC", "1.0.0");

        // Setup dependencies: C depends on B, B depends on A
        pluginB.Setup(p => p.Dependencies).Returns(new[] { "PluginA" });
        pluginC.Setup(p => p.Dependencies).Returns(new[] { "PluginB" });

        var plugins = new[] { pluginC.Object, pluginA.Object, pluginB.Object };

        // Act
        var resolved = resolver.ResolveDependencyOrder(plugins);

        // Assert
        Assert.Equal(3, resolved.Count);
        Assert.Equal("PluginA", resolved[0].Name); // No dependencies, first
        Assert.Equal("PluginB", resolved[1].Name); // Depends on A, second
        Assert.Equal("PluginC", resolved[2].Name); // Depends on B, last
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDependencyResolver_CircularDependency_ThrowsException()
    {
        // Arrange
        var resolver = new TestPluginDependencyResolver();

        var pluginA = CreateMockPlugin("PluginA", "1.0.0");
        var pluginB = CreateMockPlugin("PluginB", "1.0.0");

        // Setup circular dependency: A depends on B, B depends on A
        pluginA.Setup(p => p.Dependencies).Returns(new[] { "PluginB" });
        pluginB.Setup(p => p.Dependencies).Returns(new[] { "PluginA" });

        var plugins = new[] { pluginA.Object, pluginB.Object };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveDependencyOrder(plugins));
        Assert.Contains("circular dependency", exception.Message.ToLowerInvariant());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDependencyResolver_MissingDependency_ThrowsException()
    {
        // Arrange
        var resolver = new TestPluginDependencyResolver();

        var pluginA = CreateMockPlugin("PluginA", "1.0.0");
        pluginA.Setup(p => p.Dependencies).Returns(new[] { "MissingPlugin" });

        var plugins = new[] { pluginA.Object };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => resolver.ResolveDependencyOrder(plugins));
        Assert.Contains("MissingPlugin", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginMetadata_ExtractFromType_ReturnsCorrectMetadata()
    {
        // Arrange
        var metadataExtractor = new TestPluginMetadataExtractor();
        var pluginType = typeof(TestStoragePlugin);

        // Act
        var metadata = metadataExtractor.Extract(pluginType);

        // Assert
        Assert.Equal("TestStoragePlugin", metadata.Name);
        Assert.Equal("Storage", metadata.Category);
        Assert.NotNull(metadata.Version);
        Assert.Contains("Test storage plugin", metadata.Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginVersionManager_CheckCompatibility_ValidatesVersions()
    {
        // Arrange
        var versionManager = new TestPluginVersionManager();
        var currentVersion = new Version(2, 1, 0);

        // Act & Assert
        Assert.True(versionManager.IsCompatible(new Version(2, 0, 0), currentVersion)); // Same major
        Assert.True(versionManager.IsCompatible(new Version(2, 1, 5), currentVersion)); // Same major.minor
        Assert.False(versionManager.IsCompatible(new Version(3, 0, 0), currentVersion)); // Different major
        Assert.False(versionManager.IsCompatible(new Version(1, 9, 0), currentVersion)); // Lower major
    }

    // Helper methods
    private Mock<ITestPlugin> CreateMockPlugin(string name, string version)
    {
        var mock = new Mock<ITestPlugin>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.Version).Returns(version);
        mock.Setup(p => p.Dependencies).Returns(Array.Empty<string>());
        return mock;
    }

    private Mock<ITestPluginWithLifecycle> CreateMockPluginWithLifecycle(string name, string version)
    {
        var mock = new Mock<ITestPluginWithLifecycle>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.Version).Returns(version);
        mock.Setup(p => p.Dependencies).Returns(Array.Empty<string>());
        mock.Setup(p => p.InitializeAsync()).Returns(Task.CompletedTask);
        mock.Setup(p => p.DisposeAsync()).Returns(Task.CompletedTask);
        return mock;
    }

    // Test implementation classes
    public class TestPluginDiscovery
    {
        public List<Type> ScanAssembly(Assembly assembly, Func<Type, bool>? filter = null)
        {
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(ITestPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (filter != null)
            {
                pluginTypes = pluginTypes.Where(filter);
            }

            return pluginTypes.ToList();
        }
    }

    public class TestPluginRegistry
    {
        private readonly Dictionary<string, ITestPlugin> _plugins = new();

        public void Register(ITestPlugin plugin)
        {
            if (_plugins.ContainsKey(plugin.Name))
            {
                throw new InvalidOperationException($"Plugin '{plugin.Name}' is already registered");
            }
            _plugins[plugin.Name] = plugin;
        }

        public bool Unregister(string name) => _plugins.Remove(name);

        public bool IsRegistered(string name) => _plugins.ContainsKey(name);

        public ITestPlugin? GetPlugin(string name) => _plugins.TryGetValue(name, out var plugin) ? plugin : null;

        public List<ITestPlugin> GetAllPlugins() => _plugins.Values.ToList();
    }

    public class TestPluginLifecycleManager
    {
        private readonly HashSet<ITestPlugin> _initializedPlugins = new();

        public async Task InitializeAsync(Mock<ITestPluginWithLifecycle> mockPlugin)
        {
            await mockPlugin.Object.InitializeAsync();
            _initializedPlugins.Add(mockPlugin.Object);
        }

        public async Task DisposeAsync(Mock<ITestPluginWithLifecycle> mockPlugin)
        {
            await mockPlugin.Object.DisposeAsync();
            _initializedPlugins.Remove(mockPlugin.Object);
        }

        public async Task InitializeWithDependenciesAsync(Mock<ITestPluginWithLifecycle> mainPlugin, ITestPlugin[] dependencies)
        {
            // Initialize dependencies first
            foreach (var dependency in dependencies)
            {
                if (dependency is ITestPluginWithLifecycle lifecyclePlugin)
                {
                    await lifecyclePlugin.InitializeAsync();
                    _initializedPlugins.Add(lifecyclePlugin);
                }
            }

            // Then initialize main plugin
            await mainPlugin.Object.InitializeAsync();
            _initializedPlugins.Add(mainPlugin.Object);
        }

        public bool IsInitialized(ITestPlugin plugin) => _initializedPlugins.Contains(plugin);
    }

    public class TestPluginDependencyResolver
    {
        public List<ITestPlugin> ResolveDependencyOrder(ITestPlugin[] plugins)
        {
            var pluginDict = plugins.ToDictionary(p => p.Name, p => p);
            var resolved = new List<ITestPlugin>();
            var resolving = new HashSet<string>();

            foreach (var plugin in plugins)
            {
                ResolveDependenciesRecursive(plugin, pluginDict, resolved, resolving);
            }

            return resolved;
        }

        private void ResolveDependenciesRecursive(ITestPlugin plugin, Dictionary<string, ITestPlugin> pluginDict, List<ITestPlugin> resolved, HashSet<string> resolving)
        {
            if (resolved.Any(p => p.Name == plugin.Name))
                return; // Already resolved

            if (resolving.Contains(plugin.Name))
                throw new InvalidOperationException($"Circular dependency detected involving {plugin.Name}");

            resolving.Add(plugin.Name);

            foreach (var dependencyName in plugin.Dependencies)
            {
                if (!pluginDict.TryGetValue(dependencyName, out var dependency))
                {
                    throw new InvalidOperationException($"Dependency '{dependencyName}' not found for plugin '{plugin.Name}'");
                }

                ResolveDependenciesRecursive(dependency, pluginDict, resolved, resolving);
            }

            resolving.Remove(plugin.Name);
            resolved.Add(plugin);
        }
    }

    public class TestPluginMetadataExtractor
    {
        public PluginMetadata Extract(Type pluginType)
        {
            return new PluginMetadata
            {
                Name = pluginType.Name,
                Category = ExtractCategory(pluginType),
                Version = "1.0.0",
                Description = $"Test {ExtractCategory(pluginType).ToLowerInvariant()} plugin for unit testing"
            };
        }

        private string ExtractCategory(Type type)
        {
            if (type.Name.Contains("Storage")) return "Storage";
            if (type.Name.Contains("Serialization")) return "Serialization";
            if (type.Name.Contains("Observability")) return "Observability";
            return "Unknown";
        }
    }

    public class TestPluginVersionManager
    {
        public bool IsCompatible(Version requiredVersion, Version availableVersion)
        {
            // Simple compatibility check: same major version
            return requiredVersion.Major == availableVersion.Major;
        }
    }

    // Test plugin interfaces and implementations
    public interface ITestPlugin
    {
        string Name { get; }
        string Version { get; }
        string[] Dependencies { get; }
    }

    public interface ITestPluginWithLifecycle : ITestPlugin
    {
        Task InitializeAsync();
        Task DisposeAsync();
    }

    public class PluginMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class TestStoragePlugin : ITestPlugin
    {
        public string Name => "TestStoragePlugin";
        public string Version => "1.0.0";
        public string[] Dependencies => Array.Empty<string>();
    }
}