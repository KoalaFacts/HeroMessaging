using DotNet.Testcontainers.Builders;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Base class for RabbitMQ integration tests with Testcontainers
/// Manages RabbitMQ container lifecycle
/// </summary>
public abstract class RabbitMqIntegrationTestBase : IAsyncLifetime
{
    private RabbitMqContainer? _rabbitMqContainer;
    protected RabbitMqTransport? Transport;
    protected RabbitMqTransportOptions? Options;
    protected ILoggerFactory LoggerFactory;

    protected RabbitMqIntegrationTestBase()
    {
        LoggerFactory = NullLoggerFactory.Instance;
    }

    public async ValueTask InitializeAsync()
    {
        // Create and start RabbitMQ container
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management-alpine")
            .WithPortBinding(5672, true) // Random host port
            .WithUsername("guest")
            .WithPassword("guest")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
            .Build();

        await _rabbitMqContainer.StartAsync();

        // Create transport options
        Options = new RabbitMqTransportOptions
        {
            Host = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            VirtualHost = "/",
            UserName = "guest",
            Password = "guest",
            PrefetchCount = 10,
            UsePublisherConfirms = true,
            ConnectionTimeout = TimeSpan.FromSeconds(30)
        };

        // Create transport
        Transport = new RabbitMqTransport(Options, LoggerFactory);

        // Connect to RabbitMQ
        await Transport.ConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Transport != null)
        {
            await Transport.DisposeAsync();
        }

        if (_rabbitMqContainer != null)
        {
            await _rabbitMqContainer.StopAsync();
            await _rabbitMqContainer.DisposeAsync();
        }
    }

    /// <summary>
    /// Create a unique queue name for testing
    /// </summary>
    protected static string CreateQueueName() => $"test-queue-{Guid.NewGuid():N}";

    /// <summary>
    /// Create a unique exchange name for testing
    /// </summary>
    protected static string CreateExchangeName() => $"test-exchange-{Guid.NewGuid():N}";

    /// <summary>
    /// Create a test message envelope
    /// </summary>
    protected static TransportEnvelope CreateTestEnvelope(string content = "test message")
    {
        return new TransportEnvelope
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            ContentType = "text/plain",
            Body = System.Text.Encoding.UTF8.GetBytes(content),
            Headers = new Dictionary<string, object>
            {
                ["TestHeader"] = "TestValue"
            }
        };
    }
}
