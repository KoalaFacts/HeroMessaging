using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Transport.RabbitMQ.Configuration;

/// <summary>
/// Extension methods for configuring RabbitMQ transport
/// </summary>
public static class RabbitMqTransportExtensions
{
    /// <summary>
    /// Add RabbitMQ transport to HeroMessaging
    /// </summary>
    public static IHeroMessagingBuilder WithRabbitMq(
        this IHeroMessagingBuilder builder,
        string host,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        var options = new RabbitMqTransportOptions
        {
            Host = host
        };

        configure?.Invoke(options);

        // Register transport as singleton
        builder.Services.AddSingleton<IMessageTransport>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new RabbitMqTransport(options, loggerFactory);
        });

        return builder;
    }

    /// <summary>
    /// Add RabbitMQ transport with full connection configuration
    /// </summary>
    public static IHeroMessagingBuilder WithRabbitMq(
        this IHeroMessagingBuilder builder,
        string host,
        int port,
        string virtualHost,
        string username,
        string password,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        return builder.WithRabbitMq(host, options =>
        {
            options.Port = port;
            options.VirtualHost = virtualHost;
            options.UserName = username;
            options.Password = password;

            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Add RabbitMQ transport with SSL/TLS
    /// </summary>
    public static IHeroMessagingBuilder WithRabbitMqSsl(
        this IHeroMessagingBuilder builder,
        string host,
        string username,
        string password,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        return builder.WithRabbitMq(host, options =>
        {
            options.UseSsl = true;
            options.Port = 5671; // Default SSL port
            options.UserName = username;
            options.Password = password;

            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Configure RabbitMQ topology
    /// </summary>
    public static IHeroMessagingBuilder WithRabbitMqTopology(
        this IHeroMessagingBuilder builder,
        Action<ITopologyBuilder> configure)
    {
        var topologyBuilder = TopologyBuilder.Create();
        configure(topologyBuilder);
        var topology = topologyBuilder.Build();

        // Store topology for later configuration
        builder.Services.AddSingleton(topology);

        return builder;
    }
}
