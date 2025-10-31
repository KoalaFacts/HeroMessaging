namespace HeroMessaging.Abstractions.Plugins;

/// <summary>
/// Describes a discovered plugin with its metadata and capabilities
/// </summary>
public interface IPluginDescriptor
{
    /// <summary>
    /// Unique name of the plugin
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Version of the plugin
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Category of the plugin (Storage, Serialization, Observability, etc.)
    /// </summary>
    PluginCategory Category { get; }

    /// <summary>
    /// Description of what the plugin does
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Assembly containing the plugin
    /// </summary>
    string AssemblyName { get; }

    /// <summary>
    /// Type that implements the plugin
    /// </summary>
    Type PluginType { get; }

    /// <summary>
    /// Dependencies required by this plugin
    /// </summary>
    IReadOnlyList<string> Dependencies { get; }

    /// <summary>
    /// Configuration options supported by this plugin
    /// </summary>
    IReadOnlyDictionary<string, Type> ConfigurationOptions { get; }

    /// <summary>
    /// Features provided by this plugin
    /// </summary>
    IReadOnlyList<string> ProvidedFeatures { get; }
}

/// <summary>
/// Categories of plugins in the HeroMessaging ecosystem
/// </summary>
public enum PluginCategory
{
    Storage,
    Serialization,
    Observability,
    Security,
    Resilience,
    Validation,
    Transformation,
    Custom
}

/// <summary>
/// Attribute to mark a class as a HeroMessaging plugin
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HeroMessagingPluginAttribute : Attribute
{
    /// <summary>
    /// Gets the unique name of the plugin.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the category that this plugin belongs to.
    /// </summary>
    public PluginCategory Category { get; }

    /// <summary>
    /// Gets or sets an optional description of what the plugin does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the version of the plugin (e.g., "1.0.0").
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeroMessagingPluginAttribute"/> class.
    /// </summary>
    /// <param name="name">The unique name of the plugin. Must not be null.</param>
    /// <param name="category">The category that this plugin belongs to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public HeroMessagingPluginAttribute(string name, PluginCategory category)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = category;
    }
}