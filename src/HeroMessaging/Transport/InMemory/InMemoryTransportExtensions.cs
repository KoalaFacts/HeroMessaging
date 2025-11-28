using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring InMemoryTransport
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIServiceCollectionForInMemoryTransport
{
    /// <summary>
    /// Add InMemoryTransport to the service collection
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(
        this IServiceCollection services,
        Action<InMemoryTransportOptions>? configure = null)
    {
        var options = new InMemoryTransportOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IMessageTransport>(sp =>
            new InMemoryTransport(options, sp.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Add InMemoryTransport with a specific name
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(
        this IServiceCollection services,
        string name,
        Action<InMemoryTransportOptions>? configure = null)
    {
        var options = new InMemoryTransportOptions { Name = name };
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IMessageTransport>(sp =>
            new InMemoryTransport(options, sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
