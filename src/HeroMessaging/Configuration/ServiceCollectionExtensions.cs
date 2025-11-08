using System.Reflection;
using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
#if !NETSTANDARD2_0
using Microsoft.Extensions.Hosting;
#endif

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for IServiceCollection to configure HeroMessaging
/// </summary>
public static class HeroMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds HeroMessaging services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The HeroMessaging builder for fluent configuration</returns>
    public static IHeroMessagingBuilder AddHeroMessaging(this IServiceCollection services)
    {
        return new HeroMessagingBuilder(services);
    }

    /// <summary>
    /// Adds HeroMessaging services with configuration action
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddHeroMessaging(
        this IServiceCollection services,
        Action<IHeroMessagingBuilder> configure)
    {
        var builder = new HeroMessagingBuilder(services);
        configure(builder);
        builder.Build();
        return services;
    }

    /// <summary>
    /// Adds HeroMessaging with minimal configuration for development
    /// </summary>
    public static IServiceCollection AddHeroMessagingDevelopment(
        this IServiceCollection services,
        params IEnumerable<Assembly> assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Development()
            .UseInMemoryStorage()
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with production configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingProduction(
        this IServiceCollection services,
        string connectionString,
        params IEnumerable<Assembly> assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Production(connectionString)
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with microservice configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingMicroservice(
        this IServiceCollection services,
        string connectionString,
        params IEnumerable<Assembly> assemblies)
    {
        return services.AddHeroMessaging(builder => builder
            .Microservice(connectionString)
            .WithErrorHandling()
            .ScanAssemblies(assemblies)
        );
    }

    /// <summary>
    /// Adds HeroMessaging with custom configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingCustom(
        this IServiceCollection services,
        Action<IHeroMessagingBuilder> configure,
        params IEnumerable<Assembly> assemblies)
    {
        return services.AddHeroMessaging(builder =>
        {
            builder.ScanAssemblies(assemblies);
            configure(builder);
        });
    }
}

#if !NETSTANDARD2_0
/// <summary>
/// Extension methods for IHostBuilder to configure HeroMessaging
/// </summary>
public static class HeroMessagingHostBuilderExtensions
{
    /// <summary>
    /// Configures HeroMessaging services in the host builder
    /// </summary>
    public static IHostBuilder UseHeroMessaging(
        this IHostBuilder hostBuilder,
        Action<IHeroMessagingBuilder> configure)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessaging(configure);
        });
    }

    /// <summary>
    /// Configures HeroMessaging for development
    /// </summary>
    public static IHostBuilder UseHeroMessagingDevelopment(
        this IHostBuilder hostBuilder,
        params IEnumerable<Assembly> assemblies)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessagingDevelopment(assemblies);
        });
    }

    /// <summary>
    /// Configures HeroMessaging for production
    /// </summary>
    public static IHostBuilder UseHeroMessagingProduction(
        this IHostBuilder hostBuilder,
        string connectionString,
        params IEnumerable<Assembly> assemblies)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddHeroMessagingProduction(connectionString, assemblies);
        });
    }
}
#endif

