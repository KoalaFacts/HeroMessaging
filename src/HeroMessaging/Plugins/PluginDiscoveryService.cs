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
    private readonly IServiceCollection _services;
    private readonly ILogger<PluginDiscoveryService>? _logger;
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDiscoveryService"/> class.
    /// </summary>

    public PluginDiscoveryService(
        IPluginDiscovery discovery,
        IPluginRegistry registry,
        IPluginLoader loader,
        IServiceCollection services,
        ILogger<PluginDiscoveryService>? logger = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(loader);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }
    /// <summary>
    /// Executes discover and register plugins async.
    /// </summary>

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
