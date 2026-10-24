# HeroMessaging

**Lightweight, high-performance messaging library for .NET with saga orchestration support**

[![Build Status](https://github.com/KoalaFacts/HeroMessaging/workflows/CI/badge.svg)](https://github.com/KoalaFacts/HeroMessaging/actions)
[![NuGet](https://img.shields.io/nuget/v/HeroMessaging.svg)](https://www.nuget.org/packages/HeroMessaging/)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)

HeroMessaging is a modern, extensible messaging framework for .NET that provides in-process messaging, saga orchestration, CQRS support, and pluggable infrastructure for building distributed systems.

### Competitive goals

HeroMessaging aims to provide better performance, lower resource consumption, and a friendly public API for supported MassTransit replacement workloads. These are development goals, not measured superiority claims. The [replacement acceptance inventory](docs/masstransit-replacement-acceptance.md) tracks the functional, reliability, resource, and API requirements together with their evidence boundaries.

## Features

- **Performance Benchmarks**: BenchmarkDotNet microbenchmarks for core processing paths
- **Saga Orchestration**: Built-in state machine-based saga support with compensation
- **CQRS & Event Sourcing**: First-class support for command/query separation and events
- **Pluggable Architecture**: Extensible serialization, storage, and transport layers
- **Multi-Framework Support**: .NET 8, 9, and 10
- **Tested Core**: Automated tests, coverage gate, and cross-platform CI

### Core Capabilities

- **Message Processing**: In-memory message bus with async/await support
- **Inbox/Outbox Pattern**: Storage-backed local processing and PostgreSQL-to-RabbitMQ external queue delivery
- **Saga Orchestration**: State machine-based long-running process coordination
- **Compensation Framework**: Primitives for application-managed recovery actions
- **Timeout Handling**: Background monitoring for saga timeouts
- **Rate Limiting**: Token bucket algorithm for throughput control and backpressure
- **OpenTelemetry Integration**: Built-in observability and distributed tracing
- **Health Checks**: ASP.NET Core health check integration

### Plugin Ecosystem

**Serialization:**
- `HeroMessaging.Serialization.Json` - System.Text.Json support (default)
- `HeroMessaging.Serialization.MessagePack` - High-performance binary serialization
- `HeroMessaging.Serialization.Protobuf` - Protocol Buffers support

**Storage:**
- `HeroMessaging.Storage.SqlServer` - SQL Server message/inbox/outbox/saga storage classes; see [registration and supported scope](#storage-integration)
- `HeroMessaging.Storage.PostgreSql` - PostgreSQL inbox/outbox/saga persistence

**Transport:**
- `HeroMessaging.Transport.RabbitMQ` - RabbitMQ integration for distributed messaging

RabbitMQ `DeferAsync(delay)` holds the delivery unacknowledged until the delay expires, then requeues it. This best-effort delay occupies a consumer prefetch slot and is not durable scheduling: stopping the consumer or losing its channel can cause earlier redelivery. Use a broker-backed scheduler when the delay must survive restarts.

For external outbox delivery, configure `WithOutbox()`, `UsePostgreSqlOutbox(...)`, and `WithRabbitMq(...)`, register an `IMessageSerializer` (for example, `AddHeroMessagingJsonSerializer()`), then publish with `OutboxOptions.Destination` set to an existing durable queue. RabbitMQ publisher confirms must remain enabled. The processor claims rows with renewable PostgreSQL leases and marks them processed only after RabbitMQ confirms a routable send. Delivery is **at least once**, not atomic across PostgreSQL and RabbitMQ: a crash after broker confirmation can cause a duplicate, so consumers should deduplicate by the stable message ID. A shared-transaction PostgreSQL outbox instance cannot run the background external delivery worker; use the standalone registered storage for that worker.

For the receiving side, configure `WithInbox().UsePostgreSqlInbox(options)`, start the Inbox hosted service, and subscribe to each typed queue with `InboxTransportSubscription.SubscribeAsync<TMessage>(transport, queue, inboxProcessor, serializer)`. This subscription requires idempotency, requeues failed writes, and acknowledges RabbitMQ only after the Inbox write commits or a persisted duplicate is found. Invalid envelopes are rejected without requeue; configure a RabbitMQ dead-letter exchange if they must be retained for investigation. Processing remains asynchronous after acknowledgement; durable Inbox storage and its running worker are required for recovery. RabbitMQ redelivery is still possible, so this is at-least-once delivery rather than exactly-once execution.

`WithOutbox()` registers a hosted service that starts and stops the processor with the application host. Normal shutdown drains in-flight work; when the host shutdown deadline expires, unconfirmed external deliveries are made retryable if storage is available, or recovered after their lease expires. Applications that use a service provider without a host must start and stop `IOutboxProcessor` explicitly.

Local handlers receive shutdown cancellation. A handler that ignores cancellation may continue after the host's shutdown deadline; handlers should therefore be idempotent.

The in-process EventBus sizes its defaults from `Environment.ProcessorCount`: concurrent handlers equal the processor count (up to 32), queued handler invocations allow 128 per processor (up to 1000), and the reusable envelope pool allows 8 per processor (up to 64). These are default caps, not limits on explicit configuration. Tune only after measuring throughput, tail latency, and memory under your workload:

```csharp
builder.WithEventBus(options =>
{
    options.MaxDegreeOfParallelism = 48;
    options.BoundedCapacity = 2000;
    options.MaxPooledEnvelopes = 128;
});
```

`MaxDegreeOfParallelism` and `BoundedCapacity` must be positive. Set `MaxPooledEnvelopes` to zero to disable pooling.

If `PostgreSqlStorageOptions.AutoCreateTables` is `false`, provision the outbox table using the current `PostgreSqlOutboxStorage` schema before starting a worker. In particular, external delivery requires `retry_delay_ms BIGINT`, `lease_token UUID`, and `lease_expires_at TIMESTAMPTZ` in addition to the existing outbox columns and indexes. No old-schema migration is provided because this feature has not been released.

**Observability:**
- `HeroMessaging.Observability.OpenTelemetry` - Distributed tracing and metrics
- `HeroMessaging.Observability.HealthChecks` - ASP.NET Core health monitoring

### Source Generators

The separate `HeroMessaging.SourceGenerators` analyzer package generates code at compile time. Generator availability does not imply that every generated integration is compatible with the current runtime APIs:

- **Message Validator Generator** - Auto-generate validation from data annotations
- **Message Builder Generator** - Fluent builders for test data creation
- **Sophisticated Test Data Builder** - Advanced test builders with auto-randomization & object mothers
- **Idempotency Key Generator** - Experimental; its generated transport-context access is not aligned with the current runtime API
- **Handler Registration Generator** - Discover handlers declared in explicit namespaces
- **Saga DSL Generator** - Experimental; its generated base-class contract is not aligned with the current runtime API
- **Method Logging Generator** - Experimental; conflicts with the metrics generator on attributed partial methods
- **Metrics Instrumentation Generator** - Experimental; conflicts with the logging generator on attributed partial methods
- **Contract Testing Generator** - Auto-generate backward compatibility tests

**Quick Example:**

Install the separate analyzer package:

```bash
dotnet add package HeroMessaging.SourceGenerators
```

Define the message in `CreateOrderCommand.cs` with an explicit namespace. The current builder leaves generated fields uninitialized; reference-type properties can produce nullable warnings, so this example uses value types:

```csharp
using System.ComponentModel.DataAnnotations;
using HeroMessaging.SourceGeneration;

namespace Orders;

[GenerateValidator]
[GenerateBuilder]
public record CreateOrderCommand
{
    public Guid OrderId { get; init; }

    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }
}
```

Use the generated builder and validator in `Program.cs`:

```csharp
using Orders;

var command = CreateOrderCommandBuilder.New()
    .WithOrderId(Guid.NewGuid())
    .WithAmount(299.99m)
    .Build();

var validationResult = CreateOrderCommandValidator.Validate(command);
```

The current runtime exposes non-generic `SagaBase` and the fluent `StateMachineBuilder<TSaga>` API. Use the [Saga Orchestration Example](#saga-orchestration-example) below. The experimental Saga DSL generator emits overrides and members absent from that base class. The idempotency generator emits access to nonexistent `MessageContext.Message` and does not implement the current `IIdempotencyKeyGenerator.GenerateKey(IMessage, ProcessingContext)` contract; it does not generate a `GetIdempotencyKey()` extension method.

The logging and metrics generators both select attributed partial methods without filtering for their respective attributes. Loading the analyzer package can therefore generate duplicate implementations even when only one of these attributes is used. These integrations currently fail compilation; they are not working combined logging/metrics examples.

The [Source Generators Usage Guide](src/HeroMessaging.SourceGenerators/USAGE.md) contains additional designs and examples, including these incompatible integrations. Check generated output against the current runtime APIs before adopting those examples.

## Quick Start

### Installation

```bash
dotnet add package HeroMessaging
dotnet add package Microsoft.Extensions.DependencyInjection
```

The standalone example below uses the dependency injection package to build its
service provider with `BuildServiceProvider()`.

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
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Orders;

var services = new ServiceCollection();

services.AddHeroMessaging(builder => builder.Development());
services.AddTransient<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

#### 2. Define Messages and Handlers

Place these types in a separate file. An explicit namespace also allows the optional handler-registration generator to process them:

```csharp
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Handlers;

namespace Orders;

public sealed record OrderCreatedEvent : IEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
}

public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processing order {message.OrderId} for ${message.Amount}");
        return Task.CompletedTask;
    }
}
```

#### 3. Send and Process Messages

```csharp
await using var serviceProvider = services.BuildServiceProvider();
var messaging = serviceProvider.GetRequiredService<IHeroMessaging>();

await messaging.PublishAsync(new OrderCreatedEvent { OrderId = Guid.NewGuid(), Amount = 99.99m });
```

#### Optional In-Process Completion Receipt

`PublishAsync` waits for queue submission attempts, not handler completion or a per-handler success result. Use `PublishAndWaitAsync` through
`IEventBus`, `IEventPublisher`, or `IHeroMessaging` when the caller needs final handler pipeline outcomes:

```csharp
using var waitCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var receipt = await messaging.PublishAndWaitAsync(
    new OrderCreatedEvent { OrderId = Guid.NewGuid(), Amount = 99.99m }, waitCancellation.Token);

foreach (var handler in receipt.Handlers)
    Console.WriteLine($"{handler.HandlerType.Name}: {handler.Status} {handler.Result?.Exception?.Message}");
```

Outcomes are returned in handler registration order, including duplicate registrations. `Succeeded` and
`Failed` describe the final pipeline result after its retry/validation/error-handling policies; `Rejected`
means the queue did not accept the delivery, and `Aborted` means an accepted delivery did not complete
before the bus terminated. Pipeline failures do not necessarily mean the handler itself executed.
An empty handler list is a successful no-op; inspect `Handlers.Count` if at least one handler is required.

Cancellation at entry prevents publication. Once publication starts, cancellation only stops waiting:
handler execution and pending admission continue. A timeout/cancellation does not prove that an event
was unprocessed, and retrying publication can duplicate side effects. Ordinary `PublishAsync` does not
create completion trackers. Receipts are in-memory only, not Outbox/Inbox durability or RabbitMQ acknowledgement.
Do not await receipts from handlers on the same bus when bounded capacity or handler concurrency is exhausted;
that can deadlock. Receipt tracking has opt-in allocation and coordination costs.

### Saga Orchestration Example

This minimal single-transition definition reuses `OrderCreatedEvent` from Basic Usage. It follows the current [state-machine builder tests](tests/HeroMessaging.Tests/Unit/Orchestration/StateMachineBuilderTests.cs); it is not a complete payment/shipping workflow.

```csharp
using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Orders;

public sealed class OrderSaga : SagaBase
{
    public Guid OrderId { get; set; }
    public decimal TotalAmount { get; set; }
}

public static class OrderSagaStateMachine
{
    public static StateMachineDefinition<OrderSaga> Build()
    {
        var builder = new StateMachineBuilder<OrderSaga>();
        builder.Initially()
            .When(new Event<OrderCreatedEvent>(nameof(OrderCreatedEvent)))
            .Then(context =>
            {
                context.Instance.OrderId = context.Data.OrderId;
                context.Instance.TotalAmount = context.Data.Amount;
            })
            .TransitionTo(new State("Recorded"))
            .Finalize();

        return builder.Build();
    }
}
```

For a standalone service collection, also install `Microsoft.Extensions.Logging`:

```bash
dotnet add package Microsoft.Extensions.Logging
```

Register logging, the definition, and the development-only in-memory repository during service configuration, before building the service provider:

```csharp
using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

services.AddLogging();
services.AddHeroMessaging(builder =>
{
    builder.AddSaga<OrderSaga>(OrderSagaStateMachine.Build);
    builder.UseInMemorySagaRepository<OrderSaga>();
});
```

Registration supplies the state machine, orchestrator, repository, and `TimeProvider`. It does not itself route events to a saga; application event handlers must invoke `SagaOrchestrator<OrderSaga>.ProcessAsync` with an event carrying the intended saga correlation ID (a non-empty GUID string in `CorrelationId`). See [SagaOrchestrator](src/HeroMessaging/Orchestration/SagaOrchestrator.cs) for that API.

For example, after building `serviceProvider`, process one event in a scope:

```csharp
using HeroMessaging.Orchestration;

await using var scope = serviceProvider.CreateAsyncScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<SagaOrchestrator<OrderSaga>>();
await orchestrator.ProcessAsync(new OrderCreatedEvent
{
    CorrelationId = Guid.NewGuid().ToString(),
    OrderId = Guid.NewGuid(),
    Amount = 99.99m
});
```

This transition saves a completed saga with `CurrentState == "Recorded"`. Reuse the intended saga's correlation ID for related events; an absent, invalid, or empty GUID is ignored by the orchestrator.

### Storage Integration

The SQL Server provider contains message, inbox, outbox, dead-letter, and saga storage classes. Its [builder registration helpers](src/HeroMessaging.Storage.SqlServer/ServiceCollectionExtensions.cs) use the builder's `Services` collection. For example, register message storage, local outbox storage, and dead-letter storage with:

```csharp
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Storage.SqlServer;
using Microsoft.Extensions.DependencyInjection;

services.AddHeroMessaging(builder => builder
    .WithMediator()
    .WithEventBus()
    .UseSqlServer(new SqlServerStorageOptions
    {
        ConnectionString = connectionString,
        Schema = "dbo"
    }));
```

Registration and service resolution do not connect to the database; storage operations do. `UseSqlServer(...)` registers the three storage services above. Add `UseSqlServerSagaRepository<OrderSaga>()` to the builder to use its registered SQL Server options for a saga repository; saga event routing still follows the [Saga example](#saga-orchestration-example).

There is no `UseSqlServerInbox` helper. The specific outbox and saga helpers accept `SqlServerStorageOptions`, not connection-string overloads. SQL Server Outbox does not implement the PostgreSQL external confirmed-delivery/lease contract, and the registration tests do not establish database crash recovery or Saga concurrency safety. These remain separate [replacement acceptance requirements](docs/masstransit-replacement-acceptance.md).

### OpenTelemetry Integration

Install `HeroMessaging.Observability.OpenTelemetry` and register instrumentation before building the service provider:

```csharp
using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

services.AddHeroMessaging(builder =>
    builder.AddOpenTelemetryInstrumentation(options =>
        options.ServiceName = "Orders"));
```

This registers trace sources, meters, and transport instrumentation. For a manually assembled message-processing pipeline, add `.UseOpenTelemetry()` to `MessageProcessingPipelineBuilder`. Exporters are configured separately through `options.ConfigureTracing(...)` and `options.ConfigureMetrics(...)`, with the corresponding exporter package installed; the snippet above does not export telemetry.

### Rate Limiting

Register the limiter before building the service provider:

```csharp
using HeroMessaging.Policies;
using HeroMessaging.Abstractions.Policies;
using Microsoft.Extensions.DependencyInjection;

// Configure rate limiting
services.AddSingleton<IRateLimiter>(sp => new TokenBucketRateLimiter(
    new TokenBucketOptions
    {
        Capacity = 100,           // Burst capacity
        RefillRate = 50,          // Tokens per second
        Behavior = RateLimitBehavior.Queue,  // Queue or Reject
        MaxQueueWait = TimeSpan.FromSeconds(5),
        EnableScoping = true      // Per-message-type limiting
    },
    TimeProvider.System));
```

After all service registrations, build the provider as in Basic Usage. For an application-supplied `IMessageProcessor coreProcessor`, wrap it in a manual pipeline:

```csharp
using HeroMessaging.Processing;

var pipeline = new MessageProcessingPipelineBuilder(serviceProvider)
    .UseLogging()
    .UseRateLimiting()  // Add rate limiting decorator
    .UseRetry()
    .Build(coreProcessor);
```

Call this pipeline's `ProcessAsync` to apply the limiter. Creating it does not replace the EventBus pipeline. If the service provider was built before the limiter was registered, `UseRateLimiting()` will not find it and skips that decorator.

**Rate Limiting Behaviors:**
- `Reject`: Immediately fail when rate limit exceeded (fail-fast)
- `Queue`: Wait for tokens to become available (up to MaxQueueWait)

**Scoping Options:**
- `EnableScoping = true`: Per-message-type rate limits (isolated quotas)
- `EnableScoping = false`: Global rate limit across all message types

## Performance

The BenchmarkDotNet suite in `tests/HeroMessaging.Benchmarks` measures in-process components. It does not establish end-to-end broker p99 latency or sustained throughput guarantees. Run it on a stable machine and compare like-for-like results before setting performance budgets.

Run benchmarks:
```bash
dotnet run --project tests/HeroMessaging.Benchmarks --configuration Release --framework net10.0
```

The separate [MassTransit comparison](benchmarks/HeroMessaging.CompetitiveBenchmarks/README.md) pins MassTransit 8.5.12 and compares completed in-process command dispatch through both public APIs. Verify the workload before collecting measurements:

```bash
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks --configuration Release --framework net10.0 -- --verify
dotnet run --project benchmarks/HeroMessaging.CompetitiveBenchmarks --configuration Release --framework net10.0 --no-build -- --filter '*CommandDispatchBenchmarks*'
```

Its resource mode observes CPU, managed allocations, GC, process memory, and per-call latency in separate processes for each provider. See its README for commands, negative controls, and measurement limits. This first scenario has no broker or persistence; competitive performance and resource acceptance remain open until reproducible results satisfy the [acceptance requirements](docs/masstransit-replacement-acceptance.md#performance-and-resource-acceptance).

## Documentation

- **[MassTransit Replacement Acceptance](docs/masstransit-replacement-acceptance.md)** - Reliability, performance, resource, and API requirements
- **[Saga Orchestration Guide](docs/orchestration-pattern.md)** - Complete guide to saga patterns
- **[Choreography Pattern](docs/choreography-pattern.md)** - Event-driven choreography documentation
- **[Testing Guide](docs/testing-guide.md)** - Testing infrastructure and patterns
- **[OpenTelemetry Integration](docs/opentelemetry-integration.md)** - Observability setup
- **[Architecture Decision Records](docs/adr/)** - Design decisions and rationale

## Development

### Prerequisites

- .NET 10 SDK with Roslyn 5.9 or later (verified with SDK 10.0.401; builds every target framework)
- Docker (for integration tests)

### Building

```bash
# Restore and build
dotnet restore
dotnet build

# Run tests with Microsoft.Testing.Platform (selected by global.json)
dotnet test

# Run specific test categories
dotnet test --filter-trait "Category=Unit"
dotnet test --filter-trait "Category=Integration"

# Generate coverage report
dotnet test --project tests/HeroMessaging.Tests --filter-not-trait Category=Integration --coverlet --coverlet-output-format cobertura
reportgenerator -reports:**/coverage.cobertura*.xml -targetdir:coverage-report
```

### Code Quality

HeroMessaging maintains high standards:
- **Test Coverage**: CI checks at least 80% aggregate line coverage; 100% public API coverage is a contributor goal
- **Performance Comparison**: CI validates measurements and reports differences; percentage changes are informational, not a hard regression gate
- **Cross-Platform CI**: Windows, Linux, macOS
- **Multi-Framework**: net8.0, net9.0, net10.0

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

HeroMessaging is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details.

## Acknowledgments

Built with modern .NET best practices, inspired by patterns from:
- NServiceBus
- MassTransit
- MediatR
- Saga pattern (Enterprise Integration Patterns)

---

**Need help?** [Open an issue](https://github.com/KoalaFacts/HeroMessaging/issues) or start a [discussion](https://github.com/KoalaFacts/HeroMessaging/discussions).
