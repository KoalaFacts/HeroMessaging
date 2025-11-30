using System.Reflection;
using HeroMessaging.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Plugins;

/// <summary>
/// Service for discovering HeroMessaging plugins
/// </summary>
public class PluginDiscovery : IPluginDiscovery
{
    private readonly ILogger<PluginDiscovery>? _logger;
    private readonly IPluginRegistry _registry;

    public PluginDiscovery(IPluginRegistry registry, ILogger<PluginDiscovery>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    public Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        Assembly assembly,
        CancellationToken cancellationToken = default)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        _logger?.LogInformation("Discovering plugins in assembly: {AssemblyName}", assembly.GetName().Name);

        var plugins = new List<IPluginDescriptor>();

        try
        {
            var types = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract);

            foreach (var type in types)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check for plugin attribute
                var attribute = type.GetCustomAttribute<HeroMessagingPluginAttribute>();
                if (attribute != null)
                {
                    var descriptor = new PluginDescriptor(type, attribute);
                    plugins.Add(descriptor);
                    _logger?.LogDebug("Discovered plugin: {PluginName} ({Category})", descriptor.Name, descriptor.Category);
                    continue;
                }

                // Check if implements IMessagingPlugin
                if (typeof(IMessagingPlugin).IsAssignableFrom(type))
                {
                    var descriptor = new PluginDescriptor(type);
                    plugins.Add(descriptor);
                    _logger?.LogDebug("Discovered plugin by interface: {PluginName}", descriptor.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error discovering plugins in assembly: {AssemblyName}", assembly.GetName().Name);
        }

        return Task.FromResult<IEnumerable<IPluginDescriptor>>(plugins);
    }

    public async Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        string directory,
        string searchPattern = "*.dll",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentNullException(nameof(directory));

        if (!Directory.Exists(directory))
        {
            _logger?.LogWarning("Plugin directory does not exist: {Directory}", directory);
            return [];
        }

        _logger?.LogInformation("Discovering plugins in directory: {Directory} with pattern: {Pattern}", directory, searchPattern);

        var plugins = new List<IPluginDescriptor>();
        var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Skip non-.NET assemblies and system assemblies
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.StartsWith("System.") || fileName.StartsWith("Microsoft."))
                    continue;

                // Only load HeroMessaging-related assemblies
                if (!fileName.Contains("HeroMessaging"))
                    continue;

                var assembly = Assembly.LoadFrom(file);
                var discoveredPlugins = await DiscoverPluginsAsync(assembly, cancellationToken);
                plugins.AddRange(discoveredPlugins);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not load assembly: {File}", file);
            }
        }

        return plugins;
    }

    public async Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Discovering plugins in current AppDomain");

        var plugins = new List<IPluginDescriptor>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.GetName().Name?.Contains("HeroMessaging") == true);

        foreach (var assembly in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var discoveredPlugins = await DiscoverPluginsAsync(assembly, cancellationToken);
            plugins.AddRange(discoveredPlugins);
        }

        return plugins;
    }

    public async Task<IEnumerable<IPluginDescriptor>> DiscoverPluginsAsync(
        PluginCategory category,
        CancellationToken cancellationToken = default)
    {
        var allPlugins = await DiscoverPluginsAsync(cancellationToken);
        return allPlugins.Where(p => p.Category == category);
    }
}
