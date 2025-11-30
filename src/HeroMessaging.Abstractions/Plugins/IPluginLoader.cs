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
public sealed record PluginValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> MissingDependencies { get; init; } = Array.Empty<string>();
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
