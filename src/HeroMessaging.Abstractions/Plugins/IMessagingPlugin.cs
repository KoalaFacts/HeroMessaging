using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Plugins;

/// <summary>
/// Defines the contract for HeroMessaging plugins that extend the framework with additional functionality
/// </summary>
/// <remarks>
/// Plugins enable modular extensibility for HeroMessaging by providing self-contained units of functionality
/// that can be discovered, loaded, and initialized independently. Common plugin types include serializers,
/// storage providers, observability integrations, and custom transport implementations.
///
/// Plugin lifecycle:
/// 1. Discovery: Plugins are discovered via assembly scanning or explicit registration
/// 2. Configuration: <see cref="Configure"/> is called to register plugin services in DI container
/// 3. Initialization: <see cref="Initialize"/> is called after DI container is built
/// 4. Shutdown: <see cref="Shutdown"/> is called during application shutdown for cleanup
///
/// Plugins should follow these conventions:
/// - Name should be unique across all loaded plugins
/// - Configure should register all plugin services with the DI container
/// - Initialize should perform startup tasks (connections, resource allocation)
/// - Shutdown should cleanup resources gracefully (close connections, flush buffers)
/// - Plugins should be designed to work independently without tight coupling
/// </remarks>
public interface IMessagingPlugin
{
    /// <summary>
    /// Gets the unique name of the plugin used for identification and discovery
    /// </summary>
    /// <remarks>
    /// The name should be unique across all loaded plugins and descriptive of the plugin's purpose.
    /// Example names: "RabbitMQ.Transport", "PostgreSQL.Storage", "OpenTelemetry.Observability"
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Configures services in the dependency injection container during application startup
    /// </summary>
    /// <param name="services">The service collection to register plugin services with</param>
    /// <remarks>
    /// This method is called during the configuration phase before the service provider is built.
    /// Register all plugin services, configurations, and dependencies here.
    ///
    /// Example implementations:
    /// <code>
    /// public void Configure(IServiceCollection services)
    /// {
    ///     services.AddSingleton&lt;IMessageSerializer, JsonMessageSerializer&gt;();
    ///     services.AddOptions&lt;JsonSerializerOptions&gt;();
    ///     services.Configure&lt;JsonSerializerOptions&gt;(options =>
    ///     {
    ///         options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    ///     });
    /// }
    /// </code>
    /// </remarks>
    void Configure(IServiceCollection services);

    /// <summary>
    /// Initializes the plugin with access to the built service provider for startup tasks
    /// </summary>
    /// <param name="services">The service provider to resolve dependencies from</param>
    /// <param name="cancellationToken">Cancellation token to cancel initialization</param>
    /// <returns>A task that completes when initialization is finished</returns>
    /// <remarks>
    /// This method is called after the service provider is built, allowing plugins to resolve
    /// dependencies and perform initialization tasks such as:
    /// - Opening connections to external services
    /// - Validating configuration
    /// - Warming up caches
    /// - Starting background workers
    ///
    /// Example implementations:
    /// <code>
    /// public async Task Initialize(IServiceProvider services, CancellationToken cancellationToken)
    /// {
    ///     var logger = services.GetRequiredService&lt;ILogger&lt;MyPlugin&gt;&gt;();
    ///     var connection = services.GetRequiredService&lt;IConnection&gt;();
    ///
    ///     logger.LogInformation("Initializing {PluginName}", Name);
    ///     await connection.OpenAsync(cancellationToken);
    ///     logger.LogInformation("{PluginName} initialized successfully", Name);
    /// }
    /// </code>
    /// </remarks>
    Task Initialize(IServiceProvider services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup and resource disposal during application shutdown
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel shutdown (usually ignored)</param>
    /// <returns>A task that completes when shutdown is finished</returns>
    /// <remarks>
    /// This method is called during application shutdown to allow plugins to cleanup gracefully:
    /// - Close connections to external services
    /// - Flush pending operations
    /// - Release allocated resources
    /// - Stop background workers
    ///
    /// Implementations should be defensive and not throw exceptions during shutdown.
    ///
    /// Example implementations:
    /// <code>
    /// public async Task Shutdown(CancellationToken cancellationToken)
    /// {
    ///     try
    ///     {
    ///         _logger.LogInformation("Shutting down {PluginName}", Name);
    ///         await _connection.CloseAsync(cancellationToken);
    ///         _logger.LogInformation("{PluginName} shut down successfully", Name);
    ///     }
    ///     catch (Exception ex)
    ///     {
    ///         _logger.LogError(ex, "Error during {PluginName} shutdown", Name);
    ///         // Don't rethrow - allow graceful shutdown
    ///     }
    /// }
    /// </code>
    /// </remarks>
    Task Shutdown(CancellationToken cancellationToken = default);
}