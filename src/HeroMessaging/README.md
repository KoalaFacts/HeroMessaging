# HeroMessaging (Core Library)

**The main HeroMessaging library providing CQRS, Saga orchestration, and messaging infrastructure.**

## Overview

This is the core HeroMessaging package containing all primary messaging functionality. Most applications only need to install this package plus the desired plugins (storage, serialization, transport).

## Installation

```bash
dotnet add package HeroMessaging
```

### Framework Support

- .NET Standard 2.0
- .NET 6.0
- .NET 7.0
- .NET 8.0
- .NET 9.0

## What's Included

### Core Features
- **CQRS**: Command, Query, and Event processors
- **Saga Orchestration**: State machine-based workflows
- **Choreography**: Event-driven coordination
- **Inbox/Outbox Patterns**: Transactional messaging
- **Message Scheduling**: Delayed and recurring messages
- **Decorator Pipeline**: Extensible processing pipeline

### Built-in Components
- **In-Memory Storage**: For development and testing
- **In-Memory Transport**: For testing
- **Decorators**: Logging, validation, retry, circuit breaker, metrics
- **Error Handling**: Dead letter queues, retry policies
- **Versioning**: Message converters and version resolution

## Quick Start

See the [main README](../../README.md) for comprehensive examples.

### Basic Usage

```csharp
using HeroMessaging;

services.AddHeroMessaging(builder =>
{
    builder
        .UseInMemoryStorage()      // Development only
        .UseJsonSerialization();   // Requires HeroMessaging.Serialization.Json
});

// Send a command
await messaging.Send(new CreateOrderCommand("ORD-001", 99.99m));

// Publish an event
await messaging.Publish(new OrderCreatedEvent("ORD-001"));

// Query
var order = await messaging.Query(new GetOrderQuery("ORD-001"));
```

## Production Setup

For production, add storage and transport plugins:

```bash
dotnet add package HeroMessaging.Storage.PostgreSql
dotnet add package HeroMessaging.Transport.RabbitMQ
dotnet add package HeroMessaging.Observability.OpenTelemetry
```

```csharp
services.AddHeroMessaging(builder =>
{
    builder
        .UsePostgreSqlStorage(options =>
        {
            options.ConnectionString = "...";
        })
        .UseRabbitMqTransport(options =>
        {
            options.HostName = "rabbitmq.example.com";
        })
        .AddOpenTelemetry()
        .WithInbox()
        .WithOutbox();
});
```

## Documentation

- **[Main Documentation](../../README.md)** - Complete guide
- **[Orchestration Pattern](../../docs/orchestration-pattern.md)** - Saga workflows
- **[Choreography Pattern](../../docs/choreography-pattern.md)** - Event-driven workflows
- **[Performance Benchmarks](../../docs/PERFORMANCE_BENCHMARKS.md)** - Performance guide

## Plugins

### Storage
- [PostgreSQL](../HeroMessaging.Storage.PostgreSql/README.md)
- [SQL Server](../HeroMessaging.Storage.SqlServer/README.md)

### Serialization
- [JSON](../HeroMessaging.Serialization.Json/README.md)
- [MessagePack](../HeroMessaging.Serialization.MessagePack/README.md)
- [Protobuf](../HeroMessaging.Serialization.Protobuf/README.md)

### Transport
- [RabbitMQ](../HeroMessaging.Transport.RabbitMQ/README.md)

### Observability
- [Health Checks](../HeroMessaging.Observability.HealthChecks/README.md)
- [OpenTelemetry](../HeroMessaging.Observability.OpenTelemetry/README.md)

## License

This package is licensed under the MIT License.
