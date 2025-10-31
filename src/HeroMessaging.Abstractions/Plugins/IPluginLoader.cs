namespace HeroMessaging.Abstractions.Plugins;

/// <summary>
/// Service for loading and initializing plugins
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Load a plugin from its descriptor
    /// </summary>
    Task<IMessagingPlugin> LoadAsync(
        IPluginDescriptor descriptor,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load and configure a plugin
    /// </summary>
    Task<IMessagingPlugin> LoadAsync(
        IPluginDescriptor descriptor,
        IServiceProvider serviceProvider,
        Action<object>? configure,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a plugin can be loaded
    /// </summary>
    Task<bool> CanLoadAsync(
        IPluginDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate plugin dependencies
    /// </summary>
    Task<PluginValidationResult> ValidateAsync(
        IPluginDescriptor descriptor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of plugin validation
/// </summary>
public class PluginValidationResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin is valid and can be loaded.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the collection of validation errors that prevent the plugin from loading.
    /// Empty if there are no errors.
    /// </summary>
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the collection of validation warnings.
    /// Warnings do not prevent loading but indicate potential issues.
    /// </summary>
    public IReadOnlyCollection<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the collection of missing plugin dependencies.
    /// Lists plugins or assemblies that are required but not found.
    /// </summary>
    public IReadOnlyCollection<string> MissingDependencies { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Plugin lifecycle manager
/// </summary>
public interface IPluginLifecycleManager
{
    /// <summary>
    /// Initialize a plugin
    /// </summary>
    Task InitializeAsync(
        IMessagingPlugin plugin,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a plugin
    /// </summary>
    Task StartAsync(
        IMessagingPlugin plugin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a plugin
    /// </summary>
    Task StopAsync(
        IMessagingPlugin plugin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose a plugin
    /// </summary>
    Task DisposeAsync(
        IMessagingPlugin plugin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current state of a plugin
    /// </summary>
    PluginState GetState(IMessagingPlugin plugin);
}

/// <summary>
/// States of a plugin lifecycle
/// </summary>
public enum PluginState
{
    NotInitialized,
    Initializing,
    Initialized,
    Starting,
    Started,
    Stopping,
    Stopped,
    Failed,
    Disposed
}