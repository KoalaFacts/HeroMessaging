# HeroMessaging.Transport.RabbitMQ

**Production-ready RabbitMQ transport for HeroMessaging with connection pooling, topology management, and high availability.**

## Overview

The RabbitMQ transport provides robust, scalable message transport using RabbitMQ as the underlying broker. Features include automatic connection management, channel pooling, topology creation, and support for all RabbitMQ patterns (direct, fanout, topic, headers).

**Key Features**:
- **Connection Pooling**: Efficient connection management with automatic recovery
- **Channel Pooling**: Reusable channels for high throughput
- **Topology Management**: Automatic exchange and queue creation
- **Dead Letter Queues**: Built-in DLQ support
- **Consumer Acknowledgment**: Manual ack/nack with prefetch control
- **High Availability**: Cluster and mirrored queue support

## Installation

```bash
dotnet add package HeroMessaging.Transport.RabbitMQ
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Prerequisites

- RabbitMQ 3.8+ (3.12+ recommended)
- Management plugin enabled (optional, for UI)
- Network access to RabbitMQ server

## Quick Start

### Basic Configuration

```csharp
using HeroMessaging;
using HeroMessaging.Transport.RabbitMQ;

services.AddHeroMessaging(builder =>
{
    builder.UseRabbitMqTransport(options =>
    {
        options.HostName = "localhost";
        options.Port = 5672;
        options.UserName = "guest";
        options.Password = "guest";
        options.VirtualHost = "/";
    });
});
```

### Connection String

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.ConnectionString = "amqp://user:pass@localhost:5672/vhost";
});
```

### With SSL/TLS

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.HostName = "rabbitmq.example.com";
    options.Port = 5671; // SSL port
    options.UseSsl = true;
    options.SslOptions = new SslOption
    {
        Enabled = true,
        ServerName = "rabbitmq.example.com",
        AcceptablePolicyErrors = SslPolicyErrors.None
    };
});
```

## Configuration

### RabbitMqTransportOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HostName` | `string` | `"localhost"` | RabbitMQ server hostname |
| `Port` | `int` | `5672` | RabbitMQ server port (5671 for SSL) |
| `UserName` | `string` | `"guest"` | AMQP username |
| `Password` | `string` | `"guest"` | AMQP password |
| `VirtualHost` | `string` | `"/"` | Virtual host |
| `ConnectionTimeout` | `TimeSpan` | `30s` | Connection timeout |
| `RequestedHeartbeat` | `TimeSpan` | `60s` | Heartbeat interval |
| `AutomaticRecovery` | `bool` | `true` | Enable automatic connection recovery |
| `TopologyRecovery` | `bool` | `true` | Recreate topology on recovery |
| `PrefetchCount` | `ushort` | `10` | Messages to prefetch per consumer |
| `MaxChannels` | `int` | `100` | Maximum channels in pool |

### Advanced Configuration

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.HostName = "rabbitmq.example.com";
    options.Port = 5672;
    options.UserName = "herouser";
    options.Password = "secretpassword";
    options.VirtualHost = "/production";

    // Connection settings
    options.ConnectionTimeout = TimeSpan.FromSeconds(30);
    options.RequestedHeartbeat = TimeSpan.FromSeconds(60);
    options.AutomaticRecovery = true;
    options.TopologyRecovery = true;

    // Performance settings
    options.PrefetchCount = 20; // Higher for throughput
    options.MaxChannels = 200; // More channels for concurrency

    // Retry settings
    options.MaxRetries = 3;
    options.RetryDelay = TimeSpan.FromSeconds(5);
});
```

## Usage Patterns

### 1. Direct Messaging (Queue)

**Point-to-point messaging**:

```csharp
// Send to queue
await messaging.SendToQueue("order-processing", new ProcessOrderCommand
{
    OrderId = "ORD-001"
});

// Consume from queue
await messaging.ConsumeFromQueue("order-processing", async (ProcessOrderCommand message) =>
{
    await ProcessOrder(message);
}, cancellationToken);
```

**RabbitMQ Topology Created**:
```
Queue: order-processing
  - Durable: true
  - Auto-delete: false
  - Arguments: x-message-ttl, x-dead-letter-exchange
```

### 2. Publish-Subscribe (Fanout)

**Broadcast to all subscribers**:

```csharp
// Configure fanout exchange
builder.UseRabbitMqTransport(options =>
{
    options.DeclareExchange("order-events", ExchangeType.Fanout);
});

// Publish event
await messaging.Publish("order-events", new OrderCreatedEvent
{
    OrderId = "ORD-001"
});

// Subscribe (each subscriber gets a copy)
await messaging.Subscribe<OrderCreatedEvent>("order-events", "inventory-service", async (message) =>
{
    await UpdateInventory(message);
});

await messaging.Subscribe<OrderCreatedEvent>("order-events", "shipping-service", async (message) =>
{
    await PrepareShipment(message);
});
```

**RabbitMQ Topology Created**:
```
Exchange: order-events (fanout)
  ├─ Queue: inventory-service -> Binding
  └─ Queue: shipping-service  -> Binding
```

### 3. Topic Routing

**Route by pattern**:

```csharp
// Configure topic exchange
builder.UseRabbitMqTransport(options =>
{
    options.DeclareExchange("notifications", ExchangeType.Topic);
});

// Publish with routing key
await messaging.Publish("notifications", new EmailNotification(),
    new PublishOptions { RoutingKey = "email.order.created" });

// Subscribe with pattern
await messaging.Subscribe<Notification>("notifications", "email-handler",
    routingKey: "email.#", // All email notifications
    async (message) => await SendEmail(message));

await messaging.Subscribe<Notification>("notifications", "order-handler",
    routingKey: "*.order.*", // All order-related notifications
    async (message) => await LogOrder(message));
```

**Routing Keys**:
- `#` - matches zero or more words
- `*` - matches exactly one word
- Examples: `email.order.created`, `sms.payment.failed`

### 4. Dead Letter Queue

**Automatic DLQ for failed messages**:

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.ConfigureQueue("order-processing", queueOptions =>
    {
        queueOptions.DeadLetterExchange = "dlq";
        queueOptions.MessageTtl = TimeSpan.FromHours(24);
        queueOptions.MaxRetries = 3;
    });
});

// Failed messages go to DLQ after 3 retries
await messaging.ConsumeFromQueue("order-processing", async (ProcessOrderCommand message) =>
{
    if (!IsValid(message))
    {
        throw new InvalidOperationException("Invalid order");
        // After 3 retries, moves to DLQ
    }
    await ProcessOrder(message);
});
```

## High Availability

### Cluster Configuration

```csharp
builder.UseRabbitMqTransport(options =>
{
    // Multiple hosts for HA
    options.HostNames = new[] {
        "rabbitmq1.example.com",
        "rabbitmq2.example.com",
        "rabbitmq3.example.com"
    };

    // Client will connect to first available
    options.AutomaticRecovery = true;
});
```

### Mirrored Queues

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.ConfigureQueue("critical-orders", queueOptions =>
    {
        queueOptions.Arguments = new Dictionary<string, object>
        {
            ["x-ha-policy"] = "all", // Mirror to all nodes
            ["x-ha-sync-mode"] = "automatic"
        };
    });
});
```

### Quorum Queues (RabbitMQ 3.8+)

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.ConfigureQueue("orders", queueOptions =>
    {
        queueOptions.Arguments = new Dictionary<string, object>
        {
            ["x-queue-type"] = "quorum" // Replicated queue
        };
        queueOptions.Durable = true;
    });
});
```

## Performance Tuning

### Prefetch Count

```csharp
// Low throughput, high latency tolerance
options.PrefetchCount = 1; // Process one at a time

// High throughput, need parallelism
options.PrefetchCount = 50; // Process 50 concurrently

// Balanced
options.PrefetchCount = 10; // Default
```

### Channel Pooling

```csharp
// High concurrency scenarios
options.MaxChannels = 500; // More channels
options.ChannelPoolSize = 100; // Larger pool

// Low concurrency
options.MaxChannels = 50;
options.ChannelPoolSize = 10;
```

### Publisher Confirms

```csharp
builder.UseRabbitMqTransport(options =>
{
    options.PublisherConfirms = true; // Slower but guaranteed delivery
    options.ConfirmTimeout = TimeSpan.FromSeconds(5);
});
```

## Troubleshooting

### Common Issues

#### Issue: Connection Refused

**Symptoms**:
```
BrokerUnreachableException: None of the specified endpoints were reachable
```

**Solution**:
1. Verify RabbitMQ is running: `systemctl status rabbitmq-server`
2. Check firewall: `telnet localhost 5672`
3. Verify credentials
4. Check virtual host exists

#### Issue: Channel Shutdown

**Symptoms**:
```
AlreadyClosedException: Already closed: The AMQP operation was interrupted
```

**Solution**:
```csharp
// Enable automatic recovery
options.AutomaticRecovery = true;
options.TopologyRecovery = true;

// Increase heartbeat
options.RequestedHeartbeat = TimeSpan.FromSeconds(60);
```

#### Issue: Messages Not Being Consumed

**Symptoms**:
- Messages in queue but not processing

**Solution**:
```csharp
// Check prefetch count
options.PrefetchCount = 10; // Not 0

// Verify consumer is running
await messaging.ConsumeFromQueue("myqueue", async (message) =>
{
    // Handler must be async
    await ProcessAsync(message);
}, cancellationToken); // Don't cancel immediately
```

#### Issue: Memory Leak / Connection Buildup

**Symptoms**:
- Growing memory usage
- Too many connections in RabbitMQ Management

**Solution**:
```csharp
// Ensure proper disposal
await using var messaging = serviceProvider.GetRequiredService<IHeroMessaging>();

// Limit channel pool
options.MaxChannels = 100; // Reasonable limit
```

### Monitoring

```csharp
// Enable detailed logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddFilter("RabbitMQ", LogLevel.Debug);
});
```

**RabbitMQ Management UI**:
- http://localhost:15672 (default)
- Monitor queues, connections, channels
- View message rates and statistics

## Testing

### Integration Tests with Testcontainers

```csharp
using Testcontainers.RabbitMq;

public class RabbitMqTransportTests : IAsyncLifetime
{
    private RabbitMqContainer _container;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.12-management")
            .WithPortBinding(5672, true)
            .WithPortBinding(15672, true)
            .Build();

        await _container.StartAsync();
    }

    [Fact]
    public async Task CanSendAndReceiveMessage()
    {
        var services = new ServiceCollection();
        services.AddHeroMessaging(builder =>
        {
            builder.UseRabbitMqTransport(options =>
            {
                options.HostName = _container.Hostname;
                options.Port = _container.GetMappedPublicPort(5672);
                options.UserName = "guest";
                options.Password = "guest";
            });
        });

        var provider = services.BuildServiceProvider();
        var messaging = provider.GetRequiredService<IHeroMessaging>();

        // Test messaging
        var received = false;
        await messaging.ConsumeFromQueue("test", async (TestMessage msg) =>
        {
            received = true;
        });

        await messaging.SendToQueue("test", new TestMessage());

        await Task.Delay(1000);
        Assert.True(received);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
```

## Best Practices

1. **Enable Automatic Recovery**: Always use `AutomaticRecovery = true`
2. **Use Connection Pooling**: Don't create new connections per operation
3. **Set Appropriate Prefetch**: Balance between throughput and memory
4. **Use Quorum Queues**: For critical data in RabbitMQ 3.8+
5. **Monitor Queue Depth**: Alert on buildup
6. **Enable Publisher Confirms**: For critical messages
7. **Use Dead Letter Queues**: For failed message handling
8. **Test with Testcontainers**: Reliable integration testing

## See Also

- [Main Documentation](../../README.md)
- [PostgreSQL Storage](../HeroMessaging.Storage.PostgreSql/README.md)
- [Health Checks](../HeroMessaging.Observability.HealthChecks/README.md)
- [RabbitMQ Official Docs](https://www.rabbitmq.com/documentation.html)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
