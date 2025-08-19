using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Abstractions.Plugins;

public interface IMessagingPlugin
{
    string Name { get; }
    
    void Configure(IServiceCollection services);
    
    Task Initialize(IServiceProvider services, CancellationToken cancellationToken = default);
    
    Task Shutdown(CancellationToken cancellationToken = default);
}