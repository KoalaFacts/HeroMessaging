using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Plugins;

/// <summary>
/// Interface for messaging plugins that extend HeroMessaging functionality.
/// </summary>
public interface IMessagingPlugin
{
    /// <summary>
    /// Gets the unique name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures services required by the plugin.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    void Configure(IServiceCollection services);

    /// <summary>
    /// Initializes the plugin after dependency injection is configured.
    /// </summary>
    /// <param name="services">The service provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts down the plugin and releases resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
