using HeroMessaging.Abstractions.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Plugins;

/// <summary>
/// Service that discovers and registers plugins at startup
/// </summary>
public class PluginDiscoveryService
{
    private readonly IPluginDiscovery _discovery;
    private readonly IPluginRegistry _registry;
    private readonly IPluginLoader _loader;
    private readonly IServiceCollection _services;
    private readonly ILogger<PluginDiscoveryService>? _logger;

    public PluginDiscoveryService(
        IPluginDiscovery discovery,
        IPluginRegistry registry,
        IPluginLoader loader,
        IServiceCollection services,
        ILogger<PluginDiscoveryService>? logger = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public async Task DiscoverAndRegisterPluginsAsync()
    {
        try
        {
            var plugins = await _discovery.DiscoverPluginsAsync();
            _registry.RegisterRange(plugins);

            foreach (var plugin in plugins)
            {
                try
                {
                    // Register the plugin type in DI
                    _services.AddSingleton(plugin.PluginType);
                    _logger?.LogInformation("Registered plugin: {PluginName}", plugin.Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to register plugin: {PluginName}", plugin.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to discover plugins");
        }
    }
}