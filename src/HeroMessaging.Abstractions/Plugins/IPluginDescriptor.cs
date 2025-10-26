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
    public string Name { get; }
    public PluginCategory Category { get; }
    public string? Description { get; set; }
    public string? Version { get; set; }

    public HeroMessagingPluginAttribute(string name, PluginCategory category)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = category;
    }
}