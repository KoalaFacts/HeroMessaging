using HeroMessaging.Abstractions.Plugins;

namespace HeroMessaging.Plugins;

/// <summary>
/// Default implementation of plugin descriptor providing mutable metadata for discovered plugins
/// </summary>
/// <remarks>
/// This class serves as the default implementation of <see cref="IPluginDescriptor"/> and is used
/// by the plugin discovery system to represent loaded plugins. It automatically extracts metadata
/// from plugin types including features, dependencies, and configuration options through reflection.
/// Properties are mutable to allow for dynamic modification during plugin initialization.
/// </remarks>
public class PluginDescriptor : IPluginDescriptor
{
    /// <summary>
    /// Gets or sets the unique name of the plugin
    /// </summary>
    /// <remarks>
    /// The name is derived from the <see cref="HeroMessagingPluginAttribute"/> if present,
    /// otherwise defaults to the plugin type name. Used for plugin resolution and diagnostic logging.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the plugin
    /// </summary>
    /// <remarks>
    /// Parsed from the <see cref="HeroMessagingPluginAttribute.Version"/> property if available,
    /// otherwise defaults to 1.0.0. Used for compatibility checking and version conflict resolution.
    /// </remarks>
    public Version Version { get; set; } = new Version(1, 0, 0);

    /// <summary>
    /// Gets or sets the category of the plugin (Storage, Serialization, Observability, etc.)
    /// </summary>
    /// <remarks>
    /// Determined from the <see cref="HeroMessagingPluginAttribute"/> or inferred from the plugin
    /// type's namespace. Used to organize plugins and validate plugin compatibility with requested features.
    /// </remarks>
    public PluginCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the description of what the plugin does
    /// </summary>
    /// <remarks>
    /// Provides human-readable information about the plugin's purpose and capabilities.
    /// Sourced from the <see cref="HeroMessagingPluginAttribute.Description"/> property if available.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the name of the assembly containing the plugin
    /// </summary>
    /// <remarks>
    /// Extracted from the plugin type's assembly metadata. Used for diagnostic logging and
    /// to track which assembly a plugin was loaded from.
    /// </remarks>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the CLR type that implements the plugin
    /// </summary>
    /// <remarks>
    /// The actual plugin implementation type used for instantiation and reflection-based metadata extraction.
    /// Must be a concrete class that can be instantiated by the plugin system.
    /// </remarks>
    public Type PluginType { get; set; } = typeof(object);

    /// <summary>
    /// Gets or sets the collection of dependencies required by this plugin
    /// </summary>
    /// <remarks>
    /// Automatically extracted from the plugin type's primary constructor parameters.
    /// Interface dependencies are captured by their interface names. Used for dependency
    /// resolution and initialization ordering.
    /// </remarks>
    public IReadOnlyList<string> Dependencies { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the configuration options supported by this plugin
    /// </summary>
    /// <remarks>
    /// Automatically discovered from public writable properties on the plugin type.
    /// Maps configuration property names to their expected types, enabling type-safe
    /// configuration validation and binding.
    /// </remarks>
    public IReadOnlyDictionary<string, Type> ConfigurationOptions { get; set; } = new Dictionary<string, Type>();

    /// <summary>
    /// Gets or sets the features provided by this plugin
    /// </summary>
    /// <remarks>
    /// Automatically extracted from HeroMessaging.Abstractions interfaces implemented by the plugin type.
    /// Used to determine what capabilities the plugin offers and for feature-based plugin selection.
    /// </remarks>
    public IReadOnlyList<string> ProvidedFeatures { get; set; } = new List<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDescriptor"/> class with default values
    /// </summary>
    /// <remarks>
    /// Creates an empty descriptor with default values. All properties must be manually set
    /// when using this constructor. This constructor is primarily used for serialization or
    /// manual descriptor creation.
    /// </remarks>
    public PluginDescriptor()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginDescriptor"/> class from a plugin type
    /// </summary>
    /// <param name="pluginType">The CLR type that implements the plugin</param>
    /// <param name="attribute">Optional plugin attribute containing metadata; if null, metadata is inferred</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pluginType"/> is null</exception>
    /// <remarks>
    /// This constructor performs automatic metadata extraction:
    /// <list type="bullet">
    /// <item><description>Extracts plugin name and category from attribute or type information</description></item>
    /// <item><description>Discovers features from implemented HeroMessaging.Abstractions interfaces</description></item>
    /// <item><description>Identifies dependencies from constructor parameters</description></item>
    /// <item><description>Maps configuration options from public writable properties</description></item>
    /// </list>
    /// This is the primary constructor used by the plugin discovery system.
    /// </remarks>
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