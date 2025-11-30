using System.Reflection;

namespace HeroMessaging.Abstractions.Plugins;

/// <summary>
/// Service for discovering HeroMessaging plugins
/// </summary>
public interface IPluginDiscovery
{
    /// <summary>
    /// Discover plugins in the specified assembly
    /// </summary>
    Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        Assembly assembly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover plugins in assemblies from the specified directory
    /// </summary>
    Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        string directory,
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover plugins in the current application domain
    /// </summary>
    Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover plugins by category
    /// </summary>
    Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        PluginCategory category,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry for managing discovered plugins
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Register a plugin descriptor
    /// </summary>
    void Register(IPluginDescriptor descriptor);

    /// <summary>
    /// Register multiple plugin descriptors
    /// </summary>
    void RegisterRange(IEnumerable<IPluginDescriptor> descriptors);

    /// <summary>
    /// Get all registered plugins
    /// </summary>
    IEnumerable<IPluginDescriptor> GetAll();

    /// <summary>
    /// Get plugins by category
    /// </summary>
    IEnumerable<IPluginDescriptor> GetByCategory(PluginCategory category);

    /// <summary>
    /// Get a specific plugin by name
    /// </summary>
    IPluginDescriptor? GetByName(string name);

    /// <summary>
    /// Check if a plugin is registered
    /// </summary>
    bool IsRegistered(string name);

    /// <summary>
    /// Remove a plugin from the registry
    /// </summary>
    bool Unregister(string name);

    /// <summary>
    /// Clear all registered plugins
    /// </summary>
    void Clear();

    /// <summary>
    /// Get plugins that provide a specific feature
    /// </summary>
    IEnumerable<IPluginDescriptor> GetByFeature(string feature);
}
