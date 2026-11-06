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
        var host = Environment.GetEnvironmentVariable("RabbitMq__Host");
        if (string.IsNullOrWhiteSpace(host))
        {
            _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
                .WithPortBinding(5672, true)
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig ??= new Docker.DotNet.Models.HostConfig();
                    parameters.HostConfig.Tmpfs ??= new System.Collections.Generic.Dictionary<string, string>();
                    parameters.HostConfig.Tmpfs["/var/lib/rabbitmq"] = "rw,size=64m,mode=1777";
                })
                .WithUsername("guest")
                .WithPassword("guest")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete"))
                .Build();

            await _rabbitMqContainer.StartAsync(TestContext.Current.CancellationToken);
            host = _rabbitMqContainer.Hostname;
        }

        var port = _rabbitMqContainer?.GetMappedPublicPort(5672)
            ?? int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672", System.Globalization.CultureInfo.InvariantCulture);

        // Create transport options
        Options = new RabbitMqTransportOptions
        {
            Host = host,
            Port = port,
            VirtualHost = "/",
            UserName = "guest",
            Password = "guest",
            PrefetchCount = 10,
            UsePublisherConfirms = true,
            ConnectionTimeout = TimeSpan.FromSeconds(30)
        };

        // Create transport
        Transport = new RabbitMqTransport(Options, LoggerFactory, TimeProvider.System);

        // Connect to RabbitMQ
        await Transport.ConnectAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Transport != null)
        {
            await Transport.DisposeAsync();
        }

        if (_rabbitMqContainer != null)
        {
            await _rabbitMqContainer.StopAsync(TestContext.Current.CancellationToken);
            await _rabbitMqContainer.DisposeAsync();
        }
    }

    /// <summary>
    /// Adds a broker user with full permissions on the default virtual host. Only possible when the test owns the
    /// broker container; skipped against an external broker (<c>RabbitMq__Host</c>).
    /// </summary>
    protected async Task CreateBrokerUserAsync(string userName, string password)
    {
        if (_rabbitMqContainer is null)
            Assert.Skip("Needs the Testcontainers broker to add a user; RabbitMq__Host points at an external broker.");

        var token = TestContext.Current.CancellationToken;
        var added = await _rabbitMqContainer.ExecAsync(["rabbitmqctl", "add_user", userName, password], token);
        Assert.True(added.ExitCode == 0, $"rabbitmqctl add_user failed: {added.Stderr}");
        var permitted = await _rabbitMqContainer.ExecAsync(["rabbitmqctl", "set_permissions", "-p", "/", userName, ".*", ".*", ".*"], token);
        Assert.True(permitted.ExitCode == 0, $"rabbitmqctl set_permissions failed: {permitted.Stderr}");
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
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            ContentType = "text/plain",
            Body = System.Text.Encoding.UTF8.GetBytes(content)
        }.WithHeader("TestHeader", "TestValue");
    }
}
