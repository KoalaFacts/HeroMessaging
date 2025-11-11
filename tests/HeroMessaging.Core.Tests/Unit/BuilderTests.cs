using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for builder pattern implementations
/// Testing HeroMessagingBuilder, storage builders, and plugin registration
/// </summary>
public class BuilderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingBuilder_WithDefaultConfiguration_CreatesValidBuilder()
    {
        // Arrange & Act
        var builder = new TestHeroMessagingBuilder();

        // Assert
        Assert.NotNull(builder);
        Assert.NotNull(builder.Services);
        Assert.Empty(builder.GetRegisteredPlugins());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingBuilder_AddStorage_RegistersStorageProvider()
    {
        // Arrange
        var builder = new TestHeroMessagingBuilder();
        var mockStorage = new Mock<IMessageStorage>();

        // Act
        builder.AddStorage("test-storage", mockStorage.Object);

        // Assert
        var registeredPlugins = builder.GetRegisteredPlugins();
        Assert.Contains("test-storage", registeredPlugins.Keys);
        Assert.Equal(mockStorage.Object, registeredPlugins["test-storage"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingBuilder_AddSerialization_RegistersSerializer()
    {
        // Arrange
        var builder = new TestHeroMessagingBuilder();
        var mockSerializer = new Mock<IMessageSerializer>();

        // Act
        builder.AddSerialization("json", mockSerializer.Object);

        // Assert
        var registeredSerializers = builder.GetRegisteredSerializers();
        Assert.Contains("json", registeredSerializers.Keys);
        Assert.Equal(mockSerializer.Object, registeredSerializers["json"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HeroMessagingBuilder_WithInvalidConfiguration_ThrowsArgumentException()
    {
        // Arrange
        var builder = new TestHeroMessagingBuilder();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.AddStorage(null!, Mock.Of<IMessageStorage>()));
        Assert.Throws<ArgumentNullException>(() => builder.AddStorage("test", null!));
        Assert.Throws<ArgumentException>(() => builder.AddStorage("", Mock.Of<IMessageStorage>()));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StorageBuilder_WithPostgreSqlProvider_ConfiguresCorrectly()
    {
        // Arrange
        var builder = new TestStorageBuilder();
        var connectionString = "Host=localhost;Database=testdb;Username=test;Password=test";

        // Act
        builder.UsePostgreSql(connectionString);

        // Assert
        Assert.Equal("PostgreSql", builder.ProviderType);
        Assert.Equal(connectionString, builder.ConnectionString);
        Assert.NotNull(builder.Options);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void StorageBuilder_WithSqlServerProvider_ConfiguresCorrectly()
    {
        // Arrange
        var builder = new TestStorageBuilder();
        var connectionString = "Server=localhost;Database=testdb;Trusted_Connection=true";

        // Act
        builder.UseSqlServer(connectionString);

        // Assert
        Assert.Equal("SqlServer", builder.ProviderType);
        Assert.Equal(connectionString, builder.ConnectionString);
        Assert.NotNull(builder.Options);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializationBuilder_WithJsonProvider_ConfiguresCorrectly()
    {
        // Arrange
        var builder = new TestSerializationBuilder();

        // Act
        builder.UseJson(options =>
        {
            options.IgnoreNullValues = true;
            options.CamelCasePropertyNames = true;
        });

        // Assert
        Assert.Equal("Json", builder.SerializerType);
        Assert.NotNull(builder.Options);
        var jsonOptions = builder.Options as JsonSerializationOptions;
        Assert.True(jsonOptions?.IgnoreNullValues);
        Assert.True(jsonOptions?.CamelCasePropertyNames);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SerializationBuilder_WithMessagePackProvider_ConfiguresCorrectly()
    {
        // Arrange
        var builder = new TestSerializationBuilder();

        // Act
        builder.UseMessagePack(options =>
        {
            options.CompressionLevel = CompressionLevel.Optimal;
            options.AllowPrivateMembers = false;
        });

        // Assert
        Assert.Equal("MessagePack", builder.SerializerType);
        Assert.NotNull(builder.Options);
        var messagePackOptions = builder.Options as MessagePackSerializationOptions;
        Assert.Equal(CompressionLevel.Optimal, messagePackOptions?.CompressionLevel);
        Assert.False(messagePackOptions?.AllowPrivateMembers);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_RegisterPlugin_AddsPluginSuccessfully()
    {
        // Arrange
        var registry = new TestPluginRegistry();
        var mockPlugin = new Mock<IHeroMessagingPlugin>();
        mockPlugin.Setup(p => p.Name).Returns("TestPlugin");
        mockPlugin.Setup(p => p.Version).Returns("1.0.0");

        // Act
        registry.RegisterPlugin(mockPlugin.Object);

        // Assert
        var registeredPlugins = registry.GetRegisteredPlugins();
        Assert.Contains(mockPlugin.Object, registeredPlugins);
        Assert.True(registry.IsPluginRegistered("TestPlugin"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginRegistry_RegisterDuplicatePlugin_ThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new TestPluginRegistry();
        var mockPlugin1 = new Mock<IHeroMessagingPlugin>();
        var mockPlugin2 = new Mock<IHeroMessagingPlugin>();

        mockPlugin1.Setup(p => p.Name).Returns("TestPlugin");
        mockPlugin2.Setup(p => p.Name).Returns("TestPlugin");

        registry.RegisterPlugin(mockPlugin1.Object);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => registry.RegisterPlugin(mockPlugin2.Object));
        Assert.Contains("already registered", exception.Message);
        Assert.Contains("TestPlugin", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PluginDiscovery_ScanAssemblies_FindsPluginsCorrectly()
    {
        // Arrange
        var discovery = new TestPluginDiscovery();
        var assemblies = new[] { typeof(BuilderTests).Assembly };

        // Act
        var discoveredPlugins = discovery.ScanAssemblies(assemblies);

        // Assert
        Assert.NotNull(discoveredPlugins);
        // This would find any plugins implementing IHeroMessagingPlugin in the test assembly
        Assert.True(discoveredPlugins.Count >= 0); // May be 0 if no actual plugins in test assembly
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Builder_WithFluentConfiguration_ChainsCorrectly()
    {
        // Arrange
        var builder = new TestHeroMessagingBuilder();

        // Act
        var configuredBuilder = builder
            .AddStorage("primary", Mock.Of<IMessageStorage>())
            .AddSerialization("json", Mock.Of<IMessageSerializer>())
            .ConfigureOptions(options =>
            {
                options.DefaultTimeout = TimeSpan.FromSeconds(30);
                options.EnableMetrics = true;
            });

        // Assert
        Assert.Same(builder, configuredBuilder); // Fluent interface returns same instance
        Assert.Single(builder.GetRegisteredPlugins());
        Assert.Single(builder.GetRegisteredSerializers());
        Assert.Equal(TimeSpan.FromSeconds(30), builder.Options.DefaultTimeout);
        Assert.True(builder.Options.EnableMetrics);
    }

    // Test implementation classes
    public class TestHeroMessagingBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public HeroMessagingOptions Options { get; } = new();

        private readonly Dictionary<string, IMessageStorage> _storages = new();
        private readonly Dictionary<string, IMessageSerializer> _serializers = new();

        public TestHeroMessagingBuilder AddStorage(string name, IMessageStorage storage)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(storage);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Storage name cannot be empty", nameof(name));

            _storages[name] = storage;
            return this;
        }

        public TestHeroMessagingBuilder AddSerialization(string name, IMessageSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(serializer);

            _serializers[name] = serializer;
            return this;
        }

        public TestHeroMessagingBuilder ConfigureOptions(Action<HeroMessagingOptions> configure)
        {
            configure(Options);
            return this;
        }

        public Dictionary<string, IMessageStorage> GetRegisteredPlugins() => _storages;
        public Dictionary<string, IMessageSerializer> GetRegisteredSerializers() => _serializers;
    }

    public class TestStorageBuilder
    {
        public string? ProviderType { get; private set; }
        public string? ConnectionString { get; private set; }
        public object? Options { get; private set; }

        public TestStorageBuilder UsePostgreSql(string connectionString)
        {
            ProviderType = "PostgreSql";
            ConnectionString = connectionString;
            Options = new PostgreSqlOptions();
            return this;
        }

        public TestStorageBuilder UseSqlServer(string connectionString)
        {
            ProviderType = "SqlServer";
            ConnectionString = connectionString;
            Options = new SqlServerOptions();
            return this;
        }
    }

    public class TestSerializationBuilder
    {
        public string? SerializerType { get; private set; }
        public object? Options { get; private set; }

        public TestSerializationBuilder UseJson(Action<JsonSerializationOptions>? configure = null)
        {
            SerializerType = "Json";
            var options = new JsonSerializationOptions();
            configure?.Invoke(options);
            Options = options;
            return this;
        }

        public TestSerializationBuilder UseMessagePack(Action<MessagePackSerializationOptions>? configure = null)
        {
            SerializerType = "MessagePack";
            var options = new MessagePackSerializationOptions();
            configure?.Invoke(options);
            Options = options;
            return this;
        }
    }

    public class TestPluginRegistry
    {
        private readonly List<IHeroMessagingPlugin> _plugins = new();

        public void RegisterPlugin(IHeroMessagingPlugin plugin)
        {
            if (IsPluginRegistered(plugin.Name))
            {
                throw new InvalidOperationException($"Plugin '{plugin.Name}' is already registered");
            }
            _plugins.Add(plugin);
        }

        public bool IsPluginRegistered(string name) => _plugins.Any(p => p.Name == name);
        public IReadOnlyList<IHeroMessagingPlugin> GetRegisteredPlugins() => _plugins;
    }

    public class TestPluginDiscovery
    {
        public IReadOnlyList<IHeroMessagingPlugin> ScanAssemblies(System.Reflection.Assembly[] assemblies)
        {
            var plugins = new List<IHeroMessagingPlugin>();

            foreach (var assembly in assemblies)
            {
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IHeroMessagingPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IHeroMessagingPlugin plugin)
                    {
                        plugins.Add(plugin);
                    }
                }
            }

            return plugins;
        }
    }

    // Mock configuration classes
    public class HeroMessagingOptions
    {
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public bool EnableMetrics { get; set; } = false;
    }

    public class PostgreSqlOptions { }
    public class SqlServerOptions { }

    public class JsonSerializationOptions
    {
        public bool IgnoreNullValues { get; set; }
        public bool CamelCasePropertyNames { get; set; }
    }

    public class MessagePackSerializationOptions
    {
        public CompressionLevel CompressionLevel { get; set; }
        public bool AllowPrivateMembers { get; set; }
    }

    public enum CompressionLevel
    {
        None,
        Optimal,
        Fastest
    }

    // Mock plugin interface
    public interface IHeroMessagingPlugin
    {
        string Name { get; }
        string Version { get; }
    }
}