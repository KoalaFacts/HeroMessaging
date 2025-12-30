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
/// Result of plugin validation.
/// </summary>
public sealed record PluginValidationResult
{
    /// <summary>
    /// Gets whether the plugin passed validation.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the collection of validation errors.
    /// </summary>
    public IReadOnlyCollection<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the collection of validation warnings.
    /// </summary>
    public IReadOnlyCollection<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the collection of missing dependencies.
    /// </summary>
    public IReadOnlyCollection<string> MissingDependencies { get; init; } = [];
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
/// States of a plugin lifecycle.
/// </summary>
public enum PluginState
{
    /// <summary>
    /// Plugin has not been initialized.
    /// </summary>
    NotInitialized,

    /// <summary>
    /// Plugin is currently initializing.
    /// </summary>
    Initializing,

    /// <summary>
    /// Plugin has been initialized successfully.
    /// </summary>
    Initialized,

    /// <summary>
    /// Plugin is currently starting.
    /// </summary>
    Starting,

    /// <summary>
    /// Plugin has started successfully.
    /// </summary>
    Started,

    /// <summary>
    /// Plugin is currently stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Plugin has stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Plugin has failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Plugin has been disposed.
    /// </summary>
    Disposed
}
