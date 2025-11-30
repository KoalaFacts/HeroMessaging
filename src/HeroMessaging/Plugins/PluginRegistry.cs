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

    public PluginRegistry(ILogger<PluginRegistry>? logger = null)
    {
        _logger = logger;
    }

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

    public void RegisterRange(IEnumerable<IPluginDescriptor> descriptors)
    {
        if (descriptors == null)
            throw new ArgumentNullException(nameof(descriptors));

        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }

    public IEnumerable<IPluginDescriptor> GetAll()
    {
        return _plugins.Values.ToList();
    }

    public IEnumerable<IPluginDescriptor> GetByCategory(PluginCategory category)
    {
        return _plugins.Values.Where(p => p.Category == category).ToList();
    }

    public IPluginDescriptor? GetByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        _plugins.TryGetValue(name, out var descriptor);
        return descriptor;
    }

    public bool IsRegistered(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return _plugins.ContainsKey(name);
    }

    public bool Unregister(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (_plugins.TryRemove(name, out var descriptor))
        {
            _logger?.LogInformation("Unregistered plugin: {PluginName}", name);
            return true;
        }

        return false;
    }

    public void Clear()
    {
        var count = _plugins.Count;
        _plugins.Clear();
        _logger?.LogInformation("Cleared {Count} plugins from registry", count);
    }

    public IEnumerable<IPluginDescriptor> GetByFeature(string feature)
    {
        if (string.IsNullOrEmpty(feature))
            return [];

        return _plugins.Values
            .Where(p => p.ProvidedFeatures.Contains(feature))
            .ToList();
    }
}
