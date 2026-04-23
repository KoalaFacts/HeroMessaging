using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Plugins;

/// <summary>
/// Registry for managing discovered plugins
/// </summary>
public class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, IPluginDescriptor> _plugins = new();
    private readonly ILogger<PluginRegistry>? _logger;
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginRegistry"/> class.
    /// </summary>

    public PluginRegistry(ILogger<PluginRegistry>? logger = null)
    {
        _logger = logger;
    }
    /// <summary>
    /// Executes register.
    /// </summary>

    public void Register(IPluginDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        if (_plugins.TryAdd(descriptor.Name, descriptor))
        {
            _logger?.LogInformation("Registered plugin: {PluginName} v{Version} ({Category})",
                descriptor.Name, descriptor.Version, descriptor.Category);
        }
        else
        {
            _logger?.LogWarning("Plugin already registered: {PluginName}", descriptor.Name);
        }
    }
    /// <summary>
    /// Executes register range.
    /// </summary>

    public void RegisterRange(IEnumerable<IPluginDescriptor> descriptors)
    {
        if (descriptors == null)
            throw new ArgumentNullException(nameof(descriptors));

        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }
    /// <summary>
    /// Executes get all.
    /// </summary>

    public IEnumerable<IPluginDescriptor> GetAll()
    {
        return [.. _plugins.Values];
    }
    /// <summary>
    /// Executes get by category.
    /// </summary>

    public IEnumerable<IPluginDescriptor> GetByCategory(PluginCategory category)
    {
        return [.. _plugins.Values.Where(p => p.Category == category)];
    }
    /// <summary>
    /// Executes get by name.
    /// </summary>

    public IPluginDescriptor? GetByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        _plugins.TryGetValue(name, out var descriptor);
        return descriptor;
    }
    /// <summary>
    /// Executes is registered.
    /// </summary>

    public bool IsRegistered(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return _plugins.ContainsKey(name);
    }
    /// <summary>
    /// Executes unregister.
    /// </summary>

    public bool Unregister(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (_plugins.TryRemove(name, out _))
        {
            _logger?.LogInformation("Unregistered plugin: {PluginName}", name);
            return true;
        }

        return false;
    }
    /// <summary>
    /// Executes clear.
    /// </summary>

    public void Clear()
    {
        var count = _plugins.Count;
        _plugins.Clear();
        _logger?.LogInformation("Cleared {Count} plugins from registry", count);
    }
    /// <summary>
    /// Executes get by feature.
    /// </summary>

    public IEnumerable<IPluginDescriptor> GetByFeature(string feature)
    {
        if (string.IsNullOrEmpty(feature))
            return [];

        return [.. _plugins.Values.Where(p => p.ProvidedFeatures.Contains(feature))];
    }
}
