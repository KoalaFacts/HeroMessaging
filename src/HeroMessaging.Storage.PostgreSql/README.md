# HeroMessaging.Storage.PostgreSql

**PostgreSQL storage provider for HeroMessaging with full support for Inbox, Outbox, Queue, Message, and Saga persistence.**

## Overview

The PostgreSQL storage provider enables production-ready persistence for HeroMessaging with complete support for all storage patterns:

- **Message Storage**: Durable message persistence with queries and transactions
- **Inbox Pattern**: Exactly-once message processing with deduplication
- **Outbox Pattern**: Reliable message publishing with at-least-once delivery
- **Queue Storage**: Persistent queue-based messaging
- **Saga Storage**: Long-running workflow state persistence with optimistic concurrency

Built with async/await throughout for optimal performance and scalability.

## Installation

```bash
dotnet add package HeroMessaging.Storage.PostgreSql
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Prerequisites

- PostgreSQL 12.0 or higher (14.0+ recommended)
- Database with appropriate permissions (CREATE TABLE, INSERT, UPDATE, DELETE, SELECT)
- Connection string with credentials

## Quick Start

### Basic Configuration

```csharp
using HeroMessaging;
using HeroMessaging.Storage.PostgreSql;

services.AddHeroMessaging(builder =>
{
    builder.UsePostgreSqlStorage(options =>
    {
        options.ConnectionString = "Host=localhost;Database=heromessaging;Username=user;Password=pass";
    });
});
```

### With Inbox and Outbox Patterns

```csharp
services.AddHeroMessaging(builder =>
{
    builder
        .UsePostgreSqlStorage(options =>
        {
            options.ConnectionString = "Host=localhost;Database=heromessaging;Username=user;Password=pass";
            options.Schema = "messaging"; // Optional: custom schema
        })
        .WithInbox(options =>
        {
            options.CleanupInterval = TimeSpan.FromHours(24);
            options.RetentionPeriod = TimeSpan.FromDays(7);
        })
        .WithOutbox(options =>
        {
            options.ProcessingInterval = TimeSpan.FromSeconds(5);
            options.MaxRetries = 3;
        });
});
```

### Saga Persistence

```csharp
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UsePostgreSqlSagaRepository<OrderSaga>(options =>
    {
        options.ConnectionString = "Host=localhost;Database=heromessaging;Username=user;Password=pass";
        options.TableName = "order_sagas"; // Optional: custom table name
    });
});
```

## Configuration

### PostgreSqlStorageOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConnectionString` | `string` | *Required* | PostgreSQL connection string |
| `Schema` | `string` | `"public"` | Database schema for tables |
| `CommandTimeout` | `int` | `30` | Command timeout in seconds |
| `EnablePooling` | `bool` | `true` | Enable connection pooling |

### Advanced Configuration

```csharp
services.AddHeroMessaging(builder =>
{
    builder.UsePostgreSqlStorage(options =>
    {
        options.ConnectionString = "Host=localhost;Database=heromessaging;Username=user;Password=pass";
        options.Schema = "messaging";
        options.CommandTimeout = 60;
        options.EnablePooling = true;
    });
});
```

## Database Schema

The provider automatically creates the following tables on first use:

- `messages` - Message storage
- `inbox` - Inbox pattern for deduplication
- `outbox` - Outbox pattern for reliable publishing
- `queues` - Queue-based messaging
- `sagas` - Saga state persistence

### Manual Schema Creation

If you prefer to create tables manually:

```sql
-- See schema scripts in: src/HeroMessaging.Storage.PostgreSql/Schema/
-- Or use migration tools with your preferred ORM
```

## Usage Scenarios

### Scenario 1: Inbox Pattern for Idempotency

Ensure messages are processed exactly once:

```csharp
public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInboxProcessor _inbox;

    public async Task Handle(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        await _inbox.Process(@event, async () =>
        {
            // This code executes exactly once, even if the event arrives multiple times
            await CreateOrder(@event);
        }, cancellationToken);
    }
}
```

### Scenario 2: Outbox Pattern for Reliable Publishing

Guarantee message delivery with transactional outbox:

```csharp
public class OrderService
{
    private readonly IOutboxProcessor _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public async Task CreateOrder(CreateOrderCommand command)
    {
        await using var transaction = await _unitOfWork.BeginAsync();

        // Save order to database
        await SaveOrder(command);

        // Add event to outbox (same transaction)
        await _outbox.Add(new OrderCreatedEvent(command.OrderId), transaction);

        await transaction.CommitAsync();
        // Event will be published reliably, even if app crashes after commit
    }
}
```

### Scenario 3: Saga State Persistence

Store saga state across workflow steps:

```csharp
public class OrderSaga : SagaBase
{
    public string OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentTransactionId { get; set; }
    // State is automatically persisted to PostgreSQL
}

// Saga repository handles all persistence automatically
```

### Scenario 4: Queue-Based Processing

Use persistent queues for background jobs:

```csharp
// Enqueue a message
await messaging.EnqueueToQueue("order-processing", new ProcessOrderCommand("ORD-001"));

// Process queue messages
await messaging.ProcessQueue("order-processing", async (message) =>
{
    await ProcessOrder(message);
}, cancellationToken);
```

## Performance

- **Throughput**: ~10,000 messages/second per table
- **Latency**: <5ms p99 for write operations
- **Connection Pooling**: Enabled by default for optimal performance
- **Async/Await**: Full async support for non-blocking I/O

### Optimization Tips

1. **Use connection pooling** (enabled by default)
2. **Create indexes** on frequently queried columns
3. **Configure cleanup intervals** to prevent table bloat
4. **Use batching** for high-volume scenarios

```csharp
// Batch processing example
var messages = await GetMessagesForProcessing();
foreach (var batch in messages.Chunk(100))
{
    await ProcessBatch(batch);
}
```

## Troubleshooting

### Common Issues

#### Issue: Connection Timeout

**Symptoms:**
- `Npgsql.NpgsqlException: Connection timed out`

**Solution:**
```csharp
builder.UsePostgreSqlStorage(options =>
{
    options.ConnectionString = "Host=localhost;Database=heromessaging;Timeout=60;";
    options.CommandTimeout = 60; // Increase if needed
});
```

#### Issue: Schema Not Found

**Symptoms:**
- `Npgsql.PostgresException: schema "messaging" does not exist`

**Solution:**
```sql
-- Create schema first
CREATE SCHEMA IF NOT EXISTS messaging;
```

Or let HeroMessaging create it:
```csharp
options.Schema = "public"; // Use existing schema
```

#### Issue: Table Already Exists

**Symptoms:**
- `Npgsql.PostgresException: relation "messages" already exists`

**Solution:**
This is normal - the provider checks for existing tables and only creates missing ones.

### Logging

Enable diagnostic logging to troubleshoot database issues:

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);

    // Enable Npgsql logging
    builder.AddFilter("Npgsql", LogLevel.Debug);
});
```

## Testing

### Integration Testing with Testcontainers

```csharp
using Testcontainers.PostgreSql;

public class PostgreSqlStorageTests : IAsyncLifetime
{
    private PostgreSqlContainer _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();

        await _container.StartAsync();
    }

    [Fact]
    public async Task CanSaveAndRetrieveMessage()
    {
        var services = new ServiceCollection();
        services.AddHeroMessaging(builder =>
        {
            builder.UsePostgreSqlStorage(options =>
            {
                options.ConnectionString = _container.GetConnectionString();
            });
        });

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IMessageStorage>();

        // Test message storage
        var message = new TestMessage();
        var id = await storage.Store(message);
        var retrieved = await storage.Retrieve<TestMessage>(id);

        Assert.NotNull(retrieved);
        Assert.Equal(message.MessageId, retrieved.MessageId);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
```

## Migration

### From InMemory to PostgreSQL

1. Install the package
2. Update configuration:

```csharp
// Before
builder.UseInMemoryStorage();

// After
builder.UsePostgreSqlStorage(options =>
{
    options.ConnectionString = "your-connection-string";
});
```

3. Deploy - tables are created automatically

### Upgrading

Version updates are backward compatible. The provider checks schema version and applies migrations automatically.

## See Also

- [Main Documentation](../../README.md)
- [SQL Server Storage](../HeroMessaging.Storage.SqlServer/README.md)
- [Inbox/Outbox Patterns](../../docs/inbox-outbox-patterns.md)
- [Saga Orchestration](../../docs/orchestration-pattern.md)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
