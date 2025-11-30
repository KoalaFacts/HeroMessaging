using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for IServiceCollection to support decorator pattern
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIServiceCollectionForDecorator
{
    /// <summary>
    /// Decorates an existing service registration with a decorator implementation
    /// </summary>
    public static IServiceCollection Decorate<TService>(this IServiceCollection services, Func<TService, IServiceProvider, TService> decorator)
        where TService : class
    {
        // Find the existing service descriptor
        var existingDescriptor = services.FirstOrDefault(x => x.ServiceType == typeof(TService));
        if (existingDescriptor == null)
        {
            throw new InvalidOperationException($"Service of type {typeof(TService).Name} is not registered");
        }

        // Remove the existing registration
        services.Remove(existingDescriptor);

        // Create a new registration that wraps the original service with the decorator
        services.Add(new ServiceDescriptor(
            typeof(TService),
            serviceProvider =>
            {
                // Create the original service
                TService originalService;
                if (existingDescriptor.ImplementationFactory != null)
                {
                    originalService = (TService)existingDescriptor.ImplementationFactory(serviceProvider);
                }
                else if (existingDescriptor.ImplementationInstance != null)
                {
                    originalService = (TService)existingDescriptor.ImplementationInstance;
                }
                else if (existingDescriptor.ImplementationType != null)
                {
                    originalService = (TService)ActivatorUtilities.CreateInstance(serviceProvider, existingDescriptor.ImplementationType);
                }
                else
                {
                    throw new InvalidOperationException($"Unable to resolve original service of type {typeof(TService).Name}");
                }

                // Apply the decorator
                return decorator(originalService, serviceProvider);
            },
            existingDescriptor.Lifetime));

        return services;
    }
}
