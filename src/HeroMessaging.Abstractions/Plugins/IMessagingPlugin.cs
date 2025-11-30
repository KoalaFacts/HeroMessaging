using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Plugins;

public interface IMessagingPlugin
{
    string Name { get; }

    void Configure(IServiceCollection services);

    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
