# HeroMessaging

**Lightweight, high-performance messaging library for .NET with saga orchestration support**

[![Build Status](https://github.com/KoalaFacts/HeroMessaging/workflows/CI/badge.svg)](https://github.com/KoalaFacts/HeroMessaging/actions)
[![NuGet](https://img.shields.io/nuget/v/HeroMessaging.svg)](https://www.nuget.org/packages/HeroMessaging/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

HeroMessaging is a modern, extensible messaging framework for .NET that provides in-process messaging, saga orchestration, CQRS support, and pluggable infrastructure for building distributed systems.

## Features

- **High Performance**: <1ms p99 latency, >100K messages/second throughput
- **Saga Orchestration**: Built-in state machine-based saga support with compensation
- **CQRS & Event Sourcing**: First-class support for command/query separation and events
- **Pluggable Architecture**: Extensible serialization, storage, and transport layers
- **Multi-Framework Support**: netstandard2.0, .NET 6, 7, 8, 9
- **Production-Ready**: Comprehensive testing (80%+ coverage), performance benchmarks, cross-platform CI

### Core Capabilities

- **Message Processing**: In-memory message bus with async/await support
- **Inbox/Outbox Pattern**: Transactional message processing with at-least-once delivery
- **Saga Orchestration**: State machine-based long-running process coordination
- **Compensation Framework**: Automatic rollback support for distributed transactions
- **Timeout Handling**: Background monitoring for saga timeouts
- **OpenTelemetry Integration**: Built-in observability and distributed tracing
- **Health Checks**: ASP.NET Core health check integration

### Plugin Ecosystem

**Serialization:**
- `HeroMessaging.Serialization.Json` - System.Text.Json support (default)
- `HeroMessaging.Serialization.MessagePack` - High-performance binary serialization
- `HeroMessaging.Serialization.Protobuf` - Protocol Buffers support

**Storage:**
- `HeroMessaging.Storage.SqlServer` - SQL Server inbox/outbox/saga persistence
- `HeroMessaging.Storage.PostgreSql` - PostgreSQL inbox/outbox/saga persistence

**Transport:**
- `HeroMessaging.Transport.RabbitMQ` - RabbitMQ integration for distributed messaging

**Observability:**
- `HeroMessaging.Observability.OpenTelemetry` - Distributed tracing and metrics
- `HeroMessaging.Observability.HealthChecks` - ASP.NET Core health monitoring

## Quick Start

### Installation

```bash
dotnet add package HeroMessaging
```

For additional capabilities:
```bash
# Storage
dotnet add package HeroMessaging.Storage.SqlServer
dotnet add package HeroMessaging.Storage.PostgreSql

# Serialization
dotnet add package HeroMessaging.Serialization.MessagePack
dotnet add package HeroMessaging.Serialization.Protobuf

# Transport
dotnet add package HeroMessaging.Transport.RabbitMQ

# Observability
dotnet add package HeroMessaging.Observability.OpenTelemetry
dotnet add package HeroMessaging.Observability.HealthChecks
```

### Basic Usage

#### 1. Configure Services

```csharp
using HeroMessaging;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddHeroMessaging(builder =>
{
    builder.UseInMemoryMessageBus();
    builder.UseInMemoryInbox();
    builder.UseInMemoryOutbox();
});
```

#### 2. Define Messages and Handlers

```csharp
public record OrderCreatedEvent(Guid OrderId, decimal Amount);

public class OrderCreatedHandler : IMessageHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Processing order {message.OrderId} for ${message.Amount}");
        // Process the order...
    }
}
```

#### 3. Send and Process Messages

```csharp
var serviceProvider = services.BuildServiceProvider();
var messageBus = serviceProvider.GetRequiredService<IMessageBus>();

await messageBus.PublishAsync(new OrderCreatedEvent(Guid.NewGuid(), 99.99m));
```

### Saga Orchestration Example

```csharp
// Define saga state
public class OrderSaga : SagaBase
{
    public Guid OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public string State { get; set; } = "Pending";
}

// Build state machine
public static class OrderSagaStateMachine
{
    public static StateMachine<OrderSaga> Build()
    {
        return new StateMachineBuilder<OrderSaga>()
            .Initially(b => b
                .When<OrderCreatedEvent>()
                .Then((saga, evt) =>
                {
                    saga.OrderId = evt.OrderId;
                    saga.TotalAmount = evt.Amount;
                })
                .TransitionTo("PaymentPending"))

            .During("PaymentPending", b => b
                .When<PaymentProcessedEvent>()
                .Then((saga, evt) => { /* Process payment */ })
                .TransitionTo("PaymentComplete"))

            .During("PaymentComplete", b => b
                .When<OrderShippedEvent>()
                .Then((saga, evt) => { /* Ship order */ })
                .TransitionTo("Completed")
                .Finalize())

            .Build();
    }
}

// Register saga
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseInMemorySagaRepository<OrderSaga>();
});
```

### Storage Integration

```csharp
using HeroMessaging.Storage.SqlServer;

services.AddHeroMessaging(builder =>
{
    builder.UseSqlServerInbox("Server=localhost;Database=HeroMessaging;...");
    builder.UseSqlServerOutbox("Server=localhost;Database=HeroMessaging;...");
    builder.UseSqlServerSagaRepository<OrderSaga>("Server=localhost;Database=HeroMessaging;...");
});
```

### OpenTelemetry Integration

```csharp
using HeroMessaging.Observability.OpenTelemetry;

services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddHeroMessagingInstrumentation()
        .AddOtlpExporter());
```

## Performance

HeroMessaging is designed for high-throughput, low-latency scenarios:

- **Latency**: <1ms p99 for message processing overhead
- **Throughput**: >100K messages/second (single-threaded)
- **Memory**: <1KB allocation per message in steady state
- **Benchmarks**: Full BenchmarkDotNet suite in `tests/HeroMessaging.Benchmarks`

Run benchmarks:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release
```

## Documentation

- **[Saga Orchestration Guide](docs/orchestration-pattern.md)** - Complete guide to saga patterns
- **[Choreography Pattern](docs/choreography-pattern.md)** - Event-driven choreography documentation
- **[Testing Guide](docs/testing-guide.md)** - Testing infrastructure and patterns
- **[OpenTelemetry Integration](docs/opentelemetry-integration.md)** - Observability setup
- **[Architecture Decision Records](docs/adr/)** - Design decisions and rationale

## Development

### Prerequisites

- .NET 6.0 SDK or higher
- Docker (for integration tests)

### Building

```bash
# Restore and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Run specific test categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

### Code Quality

HeroMessaging maintains high standards:
- **Test Coverage**: 80%+ (100% for public APIs)
- **Performance Regression Detection**: <10% tolerance
- **Cross-Platform CI**: Windows, Linux, macOS
- **Multi-Framework**: netstandard2.0, net6.0, net7.0, net8.0, net9.0

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Workflow

1. **Write tests first** (TDD approach)
2. **Implement minimal code** to pass tests
3. **Refactor** while keeping tests green
4. **Ensure coverage** meets 80% minimum
5. **Run benchmarks** to verify performance

See [CLAUDE.md](CLAUDE.md) for detailed development guidelines.

## Security

Please see [SECURITY.md](SECURITY.md) for security policies and vulnerability reporting.

## Roadmap

- **v1.0**: Production-ready core with saga orchestration
- **Future**: Additional storage providers, transport options, and observability integrations

See [CHANGELOG.md](CHANGELOG.md) for version history and migration guides.

## License

HeroMessaging is licensed under the MIT License. See [LICENSE](LICENSE) for details.

## Acknowledgments

Built with modern .NET best practices, inspired by patterns from:
- NServiceBus
- MassTransit
- MediatR
- Saga pattern (Enterprise Integration Patterns)

---

**Need help?** [Open an issue](https://github.com/KoalaFacts/HeroMessaging/issues) or start a [discussion](https://github.com/KoalaFacts/HeroMessaging/discussions).
