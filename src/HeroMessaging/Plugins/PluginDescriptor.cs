using HeroMessaging.Abstractions.Plugins;

namespace HeroMessaging.Plugins;

/// <summary>
/// Default implementation of plugin descriptor
/// </summary>
public class PluginDescriptor : IPluginDescriptor
{
    /// <summary>
    /// Gets or sets name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets version.
    /// </summary>
    public Version Version { get; set; } = new Version(1, 0, 0);
    /// <summary>
    /// Gets or sets category.
    /// </summary>
    public PluginCategory Category { get; set; }
    /// <summary>
    /// Gets or sets description.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Gets or sets assembly name.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets plugin type.
    /// </summary>
    public Type PluginType { get; set; } = typeof(object);
    /// <summary>
    /// Gets or sets dependencies.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; set; } = [];
    /// <summary>
    /// Gets or sets configuration options.
    /// </summary>
    public IReadOnlyDictionary<string, Type> ConfigurationOptions { get; set; } = new Dictionary<string, Type>();
    /// <summary>
    /// Gets or sets provided features.
    /// </summary>
    public IReadOnlyList<string> ProvidedFeatures { get; set; } = [];
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDescriptor"/> class.
    /// </summary>

    public PluginDescriptor()
    {
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDescriptor"/> class.
    /// </summary>

    public PluginDescriptor(Type pluginType, HeroMessagingPluginAttribute? attribute = null)
    {
        PluginType = pluginType ?? throw new ArgumentNullException(nameof(pluginType));
        AssemblyName = pluginType.Assembly.GetName().Name ?? pluginType.Assembly.FullName ?? string.Empty;

        if (attribute != null)
        {
            Name = attribute.Name;
            Category = attribute.Category;
            Description = attribute.Description;

            if (!string.IsNullOrEmpty(attribute.Version) && Version.TryParse(attribute.Version, out var version))
            {
                Version = version;
            }
        }
        else
        {
            Name = pluginType.Name;
            Category = DetermineCategory(pluginType);
        }

        // Extract features and dependencies from type
        ExtractMetadata(pluginType);
    }

    private void ExtractMetadata(Type pluginType)
    {
        var features = new List<string>();
        var dependencies = new List<string>();
        var configOptions = new Dictionary<string, Type>();

        // Check implemented interfaces for features
        foreach (var iface in pluginType.GetInterfaces())
        {
            if (iface.Namespace?.StartsWith("HeroMessaging.Abstractions") == true)
            {
                features.Add(iface.Name);
            }
        }

        // Check constructor parameters for dependencies
        var constructors = pluginType.GetConstructors();
        if (constructors.Length > 0)
        {
            var ctor = constructors[0]; // Use primary constructor
            foreach (var param in ctor.GetParameters())
            {
                if (param.ParameterType.IsInterface)
                {
                    dependencies.Add(param.ParameterType.Name);
                }
            }
        }

        // Check for configuration properties
        foreach (var prop in pluginType.GetProperties())
        {
            if (prop.CanWrite && prop.PropertyType.IsPublic)
            {
                configOptions[prop.Name] = prop.PropertyType;
            }
        }

        ProvidedFeatures = features;
        Dependencies = dependencies;
        ConfigurationOptions = configOptions;
    }

    private static PluginCategory DetermineCategory(Type pluginType)
    {
        var ns = pluginType.Namespace ?? string.Empty;

        if (ns.Contains("Storage")) return PluginCategory.Storage;
        if (ns.Contains("Serialization")) return PluginCategory.Serialization;
        if (ns.Contains("Observability")) return PluginCategory.Observability;
        if (ns.Contains("Security")) return PluginCategory.Security;
        if (ns.Contains("Resilience")) return PluginCategory.Resilience;
        if (ns.Contains("Validation")) return PluginCategory.Validation;
        if (ns.Contains("Transformation")) return PluginCategory.Transformation;

        return PluginCategory.Custom;
    }
}
