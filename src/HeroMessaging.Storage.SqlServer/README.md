# HeroMessaging.Storage.SqlServer

**SQL Server storage provider for HeroMessaging with full support for Inbox, Outbox, Queue, Message, and Saga persistence.**

## Overview

The SQL Server storage provider enables production-ready persistence for HeroMessaging with complete support for all storage patterns. Optimized for SQL Server 2019+ with full async/await support and connection pooling.

## Installation

```bash
dotnet add package HeroMessaging.Storage.SqlServer
```

### Framework Support

- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## Prerequisites

- SQL Server 2019 or higher (2022 recommended)
- Azure SQL Database supported
- Database with appropriate permissions (CREATE TABLE, INSERT, UPDATE, DELETE, SELECT)

## Quick Start

```csharp
using HeroMessaging;
using HeroMessaging.Storage.SqlServer;

services.AddHeroMessaging(builder =>
{
    builder.UseSqlServerStorage(options =>
    {
        options.ConnectionString = "Server=localhost;Database=HeroMessaging;Trusted_Connection=True;";
    });
});
```

### Azure SQL Database

```csharp
builder.UseSqlServerStorage(options =>
{
    options.ConnectionString = "Server=tcp:yourserver.database.windows.net,1433;" +
                               "Database=HeroMessaging;" +
                               "User ID=yourusername;" +
                               "Password=yourpassword;" +
                               "Encrypt=True;";
});
```

### With Saga Persistence

```csharp
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseSqlServerSagaRepository<OrderSaga>(options =>
    {
        options.ConnectionString = "your-connection-string";
        options.TableName = "OrderSagas";
    });
});
```

## Configuration

### SqlServerStorageOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConnectionString` | `string` | *Required* | SQL Server connection string |
| `Schema` | `string` | `"dbo"` | Database schema for tables |
| `CommandTimeout` | `int` | `30` | Command timeout in seconds |
| `EnableRetryOnFailure` | `bool` | `true` | Automatic retry for transient failures |

## Performance

- **Throughput**: ~8,000 messages/second per table
- **Latency**: <5ms p99 for write operations
- **Connection Pooling**: Enabled by default
- **Resilience**: Automatic retry for transient failures

## Troubleshooting

### Common Issues

#### Issue: Connection Failed

**Symptoms:**
- `SqlException: A network-related or instance-specific error`

**Solution:**
1. Verify SQL Server is running
2. Check firewall settings
3. Ensure TCP/IP is enabled in SQL Server Configuration Manager

#### Issue: Login Failed

**Symptoms:**
- `SqlException: Login failed for user`

**Solution:**
```csharp
// Use Windows Authentication
options.ConnectionString = "Server=localhost;Database=HeroMessaging;Trusted_Connection=True;";

// Or SQL Authentication
options.ConnectionString = "Server=localhost;Database=HeroMessaging;User Id=sa;Password=YourPassword;";
```

## See Also

- [Main Documentation](../../README.md)
- [PostgreSQL Storage](../HeroMessaging.Storage.PostgreSql/README.md)
- [Saga Orchestration](../../docs/orchestration-pattern.md)

## License

This package is part of HeroMessaging and is licensed under the MIT License.
